using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using HidSharp;
using HidSharp.Reports;
using HidSharp.Reports.Encodings;
using HidSharp.Reports.Input;

namespace Vulcan.NET
{
    public class TestArgs : EventArgs
    {
        public TestArgs(byte pressedKeyData, bool down)
        {
            Down = down;
            PressedKeyData = pressedKeyData;
        }

        public bool Down { get; set; }
        public byte PressedKeyData { get; set; }
    }

    public class KeyPressedArgs : EventArgs
    {
        public KeyPressedArgs(Key key, bool isPressed)
        {
            Key = key;
            IsPressed = isPressed;
        }

        public Key Key { get; set; }
        public bool IsPressed { get; set; }
    }

    public class VolumeKnobArgs : EventArgs
    {
        public VolumeKnobArgs(byte id, bool isPressed)
        {
            Id = id;
            IsPressed = isPressed;
        }

        public bool IsPressed { get; set; }
        public byte Id { get; set; }
    }
    public class VolumeKnobFxArgs : EventArgs
    {
        public VolumeKnobFxArgs(byte id, byte data)
        {
            Id = id;
            Data = data;
        }

        public byte Data { get; set; }
        public byte Id { get; set; }
    }
    public class VolumeKnDirectionArgs : EventArgs
    {
        public VolumeKnDirectionArgs(byte id, bool data)
        {
            Id = id;
            TurnedRight = data;
            TurnedLeft = !data;
        }


        public bool TurnedRight { get; set; }
        public bool TurnedLeft { get; set; }
        public byte Id { get; set; }
    }


    /// <summary>
    /// Class representing a vulcan Keyboard. Can only interface with one at a time
    /// </summary>
    public sealed class VulcanKeyboard : IDisposable
    {
        public event EventHandler<TestArgs> TestKeyPressedReceived;
        public event EventHandler<KeyPressedArgs> KeyPressedReceived;
        public event EventHandler<VolumeKnobArgs> VolumeKnobPressedReceived;
        public event EventHandler<VolumeKnobFxArgs> VolumeKnobFxPressedReceived;
        public event EventHandler<VolumeKnDirectionArgs> VolumeKnobTurnedReceived;


        private const int MaxTries = 100;
        private const int VendorId = 0x1E7D;
        private const uint LedUsagePage = 0x0001;
        private const uint LedUsage = 0x0000;
        private static readonly int[] ProductIds = new int[] { 0x307A, 0x3098 };
        private static readonly byte[] ColorPacketHeader = new byte[5] { 0x00, 0xa1, 0x01, 0x01, 0xb4 };

        private readonly HidDevice _ledDevice;
        private readonly HidStream _ledStream;
        private readonly HidDevice _ctrlDevice;
        private readonly HidStream _ctrlStream;
        private readonly HidDevice _inputDevice;
        private readonly HidStream _inputStream;
        private readonly CancellationTokenSource _source;
        private readonly HidDeviceInputReceiver _receiver;
        private readonly Task _listenTask;
        private readonly byte[] _keyColors = new byte[444];//64 * 6 + 60
        private readonly List<HidStream> streamsToDispose = new List<HidStream>();

        private VulcanKeyboard(HidDevice ledDevice, HidStream ledStream, HidDevice ctrlDevice, HidStream ctrlStream)
        {
            _ledDevice = ledDevice;
            _ledStream = ledStream;
            _ctrlDevice = ctrlDevice;
            _ctrlStream = ctrlStream;
            _source = new CancellationTokenSource();
        }



        private static readonly byte[] normal_key_header = new byte[] { 0x03, 0x00, 0xFB };
        private static readonly byte[] easyshift_key_header = new byte[] { 0x03, 0x00, 0x0A };
        private static readonly byte[] volumneknob1_key_header = new byte[] { 0x03, 0x00, 0x0B };
        private static readonly byte[] volumneknob2_key_header = new byte[] { 0x03, 0x00, 0xCC };
        private static readonly byte[] volumneknobfx_key_header = new byte[] { 0x03, 0x00, 0x0C };

        private static readonly Dictionary<byte, Key> KeyToKeyDataMapping = new Dictionary<byte, Key>()
        {
            { 0x11, Key.ESC },
            { 0x12, Key.TILDE },
            { 0x14, Key.TAB },
            { 0x16, Key.LEFT_SHIFT },
            { 0x17, Key.LEFT_CONTROL },
            { 0x13, Key.D1 },
            { 0x1A, Key.Q },
            { 0x1C, Key.A },
            { 29, Key.ISO_BACKSLASH },
            { 31, Key.LEFT_WINDOWS },
            { 16, Key.F1 },
            { 25, Key.D2 },
            { 27, Key.W },
            { 37, Key.S },
            { 38, Key.Z },
            { 39, Key.LEFT_ALT },
            { 24, Key.F2 },
            { 34, Key.D3 },
            { 36, Key.E },
            { 44, Key.D },
            { 45, Key.X },
            { 33, Key.F3 },
            { 35, Key.D4 },
            { 43, Key.R },
            { 53, Key.F },
            { 46, Key.C },
            { 32, Key.F4 },
            { 42, Key.D5 },
            { 51, Key.T },
            { 52, Key.G },
            { 54, Key.V },
            { 41, Key.D6 },
            { 59, Key.Y },
            { 61, Key.H },
            { 62, Key.B },
            { 63, Key.SPACE },
            { 40, Key.F5 },
            { 49, Key.D7 },
            { 60, Key.U },
            { 68, Key.J },
            { 71, Key.N },
            { 48, Key.F6 },
            { 66, Key.D8 },
            { 67, Key.I },
            { 69, Key.K },
            { 70, Key.M },
            { 56, Key.F7 },
            { 65, Key.D9 },
            { 76, Key.O },
            { 77, Key.L },
            { 78, Key.OEMCOMMA },
            { 57, Key.F8 },
            { 74, Key.D0 },
            { 84, Key.P },
            { 85, Key.SEMICOLON },
            { 86, Key.OEMPERIOD },
            { 103, Key.RIGHT_ALT },
            { 75, Key.OEMMINUS },
            { 91, Key.OPEN_BRACKET },
            { 93, Key.APOSTROPHE },
            { 94, Key.FORWARD_SLASH },
            { 119, Key.FN_Key },
            { 64, Key.F9 },
            { 83, Key.EQUALS },
            { 92, Key.CLOSE_BRACKET },
            //{ 100, Key.BACKSLASH }, // ANSI only
            { 110, Key.RIGHT_SHIFT },
            { 127, Key.APPLICATION_SELECT },
            { 72, Key.F10 },
            { 80, Key.F11 },
            { 81, Key.F12 },
            { 73, Key.BACKSPACE },
            { 107, Key.ENTER },
            { 135, Key.RIGHT_CONTROL },
            { 100, Key.ISO_HASH },
            { 88, Key.PRINT_SCREEN },
            { 89, Key.INSERT },
            { 90, Key.DELETE },
            { 109, Key.ARROW_LEFT },
            { 96, Key.SCROLL_LOCK },
            { 97, Key.HOME },
            { 98, Key.END },
            { 108, Key.ARROW_UP },
            { 117, Key.ARROW_DOWN },
            { 104, Key.PAUSE_BREAK },
            { 105, Key.PAGE_UP },
            { 106, Key.PAGE_DOWN },
            { 125, Key.ARROW_RIGHT },
            { 113, Key.NUM_LOCK },
            { 114, Key.NUMPAD7 },
            { 115, Key.NUMPAD4 },
            { 116, Key.NUMPAD1 },
            { 133, Key.NUMPAD0 },
            { 121, Key.DIVIDE },
            { 122, Key.NUMPAD8 },
            { 123, Key.NUMPAD5 },
            { 124, Key.NUMPAD2 },
            { 129, Key.MULTIPLY },
            { 130, Key.NUMPAD9 },
            { 131, Key.NUMPAD6 },
            { 132, Key.NUMPAD3 },
            { 141, Key.DECIMAL },
            { 137, Key.SUBTRACT },
            { 138, Key.ADD },
            { 140, Key.NUM_ENTER },
        };


        public static byte[] TrimEnd(byte[] array)
        {
            int lastIndex = Array.FindLastIndex(array, b => b != 0);

            Array.Resize(ref array, lastIndex + 1);

            return array;
        }
  

        /// <summary>
        /// Initializes the keyboard. Returns a keyboard object if initialized successfully or null otherwise
        /// </summary>
        public static VulcanKeyboard Initialize()
        {
            var devices = DeviceList.Local.GetHidDevices(vendorID: VendorId)
                        .Where(d => ProductIds.Any(id => id == d.ProductID));

            if (!devices.Any())
                return null;

            try
            {
                HidDevice ledDevice = GetFromUsages(devices, LedUsagePage, LedUsage);
                HidDevice ctrlDevice = devices.First(d => d.GetMaxFeatureReportLength() > 50);
                var inputDevices = devices.Where(d => d.GetMaxInputReportLength() >= 5);

                HidStream ledStream = null;
                HidStream ctrlStream = null;
                HidStream inputStream = null;
                Func<ReportDescriptor, HidDeviceInputReceiver> generateHIDInputReceiver = (desc) =>
                {
                    return new HidDeviceInputReceiver(100, desc);
                };
                if ((ctrlDevice?.TryOpen(out ctrlStream) ?? false) && (ledDevice?.TryOpen(out ledStream) ?? false))
                {
                    VulcanKeyboard kb = new VulcanKeyboard(ledDevice, ledStream, ctrlDevice, ctrlStream);

                    if (kb.SendCtrlInitSequence())
                    {
                        foreach (var item in inputDevices)
                        {
                            var data1 = item.GetReportDescriptor();
                            //var receiver2= data1.CreateHidDeviceInputReceiver();
                            var receiver1 = generateHIDInputReceiver(data1);

                            receiver1.Received += kb.Receiver1Received;
                            if (item.TryOpen(out var str))
                            {
                                receiver1.Start(str);
                                kb.streamsToDispose.Add(str);
                            }
                        }

                        return kb;
                    }
                }
                else
                {
                    ctrlStream?.Close();
                    ledStream?.Close();
                }
            }
            catch
            { }

            return null;
        }

        private void Receiver1Received(object sender, ByteEventArgs b)
        {
            int offset = 0;

            var memory = new ReadOnlySpan<byte>(b.Bytes);

            while (true)
            {
                var bytes = memory[offset..(offset + 5)];
                
                if ((bytes[1] + bytes[4] == 6 && bytes[3] == 255))
                {
                    offset += 5;
                    continue;
                }
                if (bytes.Length < offset + 5 || bytes[0] + bytes[1 ] + bytes[2] + bytes[3] + bytes[4] == 0)
                    return;

                //KeyPressedReceived?.Invoke(sender, b);
                //Console.WriteLine("Recvd: " + string.Join(" ", bytes.Take(10).Select(x => x.ToString("X2"))));a

                if (bytes.Length > 3)
                {
                    var header_ = bytes[0..3];
                    var bb = bytes[3..^0];
                    var data = bytes[3];
                    var key = bb[0];
                    var down = bytes[4] > 0;

                    if (bytes[0..(normal_key_header.Length)].SequenceEqual(normal_key_header))
                    {
                        KeyPressedReceived?.Invoke(this, new KeyPressedArgs(KeyToKeyDataMapping[key], down));
                    }
                    else if (bytes[0..(easyshift_key_header.Length)].SequenceEqual(easyshift_key_header))
                    {
                        KeyPressedReceived?.Invoke(this, new KeyPressedArgs(Key.CAPS_LOCK, down));
                    }
                    else if (bytes[0..(volumneknob1_key_header.Length)].SequenceEqual(volumneknob1_key_header))
                    {
                        VolumeKnobPressedReceived?.Invoke(this, new VolumeKnobArgs(key, down));
                        //Console.WriteLine($"knop: {key_:X2} -> \t\t" + string.Join(" ", data_.Select(x => $"{x:X2}")));
                    }
                    else if (bytes[0..(volumneknobfx_key_header.Length)].SequenceEqual(volumneknobfx_key_header))
                    {
                        VolumeKnobFxPressedReceived?.Invoke(this, new VolumeKnobFxArgs(key, data));
                    }
                    else if (bytes[0..(volumneknob2_key_header.Length)].SequenceEqual(volumneknob2_key_header))
                    {
                        VolumeKnobTurnedReceived?.Invoke(this, new VolumeKnDirectionArgs(key, data < 128));
                    }


                    //TestKeyPressedReceived?.Invoke(this, new TestArgs(key, down));
                    //if (header_.SequenceEqual(normal_key_header))
                    //{
                    //    Console.WriteLine($"Key {(down ? "down" : "up  ")}: {key_:X2} -> \t\t" + string.Join(" ", data_.Select(x => $"{x:X2}")));
                    //}
                    //else if (header_.SequenceEqual(volumneknob1_key_header))
                    //{
                    //    Console.WriteLine($"knop {(down ? "down" : "up  ")}: {key_:X2} -> \t\t" + string.Join(" ", data_.Select(x => $"{x:X2}")));
                    //}
                    //Console.WriteLine($"Recvd {DateTime.Now:HH:mm:ss}: " + string.Join(" ", bytes.ToArray().Select(x=>x.ToString("X2"))));
                }

                offset += 5;
            }

        }

        private void StartListener()
        {
            _listenTask.Start();
        }

        #region Public Methods
        /// <summary>
        /// Sets the whole keyboard to a color
        /// </summary>
        public void SetColor(Color clr)
        {
            foreach (LedKey key in (LedKey[])Enum.GetValues(typeof(LedKey)))
                SetKeyColor(key, clr);
        }

        /// <summary>
        /// Set the colors of all the keys in the dictionary
        /// </summary>
        public void SetColors(Dictionary<LedKey, Color> keyColors)
        {
            foreach (var key in keyColors)
                SetKeyColor(key.Key, key.Value);
        }

        /// <summary>
        /// Set the colors of all the keys in the dictionary
        /// </summary>
        public void SetColors(Dictionary<int, Color> keyColors)
        {
            foreach (var key in keyColors)
                SetKeyColor(key.Key, key.Value);
        }

        /// <summary>
        /// Sets a given key to a given color
        /// </summary>
        public void SetKeyColor(LedKey key, Color clr)
        {
            int offset = ((int)key / 12 * 36) + ((int)key % 12);
            _keyColors[offset + 0] = clr.R;
            _keyColors[offset + 12] = clr.G;
            _keyColors[offset + 24] = clr.B;
        }

        /// <summary>
        /// Sets a given key to a given color
        /// </summary>
        public void SetKeyColor(int key, Color clr)
        {
            int offset = ((int)key / 12 * 36) + ((int)key % 12);
            _keyColors[offset + 0] = clr.R;
            _keyColors[offset + 12] = clr.G;
            _keyColors[offset + 24] = clr.B;
        }

        /// <summary>
        /// Writes data to the keyboard
        /// </summary>
        public async Task<bool> Update()
        {

            return await Task.Run(() => WriteColorBuffer());
        }

        /// <summary>
        /// Disconnects from the keyboard. Call this last
        /// </summary>
        public void Disconnect()
        {

            _source?.Cancel();
            streamsToDispose.ForEach(x => { x.Dispose(); });
            _ctrlStream?.Dispose();
            _ledStream?.Dispose();

        }

        #endregion

        #region Private Hid Methods
        private bool WriteColorBuffer()
        {
            //structure of the data: 
            //header *5
            //data *60

            //0x00 * 1
            //data *64

            //0x00 * 1
            //data *64

            //0x00 * 1
            //data *64

            //0x00 * 1
            //data *64

            //0x00 * 1
            //data *64

            //0x00 * 1
            //data *64

            byte[] packet = new byte[65 * 7];

            ColorPacketHeader.CopyTo(packet, 0);//header at the beginning of the first packet
            Array.Copy(_keyColors, 0,
                        packet, ColorPacketHeader.Length,
                        65 - ColorPacketHeader.Length);//copy the first 60 bytes of color data to the packet
                                                       //so 60 data + 5 header fits in a packet
            try
            {

                for (int i = 1; i <= 6; i++)//each chunk consists of the byte 0x00 and 64 bytes of data after that
                {
                    Array.Copy(_keyColors, (i * 64) - 4,//each packet holds 64 except for the first one, hence we subtract 4
                                packet, i * 65 + 1,
                                64);

                    //_ledStream.Write(packet);
                }
                _ledStream.Write(packet);

                return true;
            }
            catch (Exception e)
            {
                Disconnect();
                return false;
            }
        }

        private bool SendCtrlInitSequence()
        {
            var result =
                //GetCtrlReport(0x0f) &&
                SetCtrlReport(CtrlReports._0x15) &&
                WaitCtrlDevice() &&
                //SetCtrlReport(CtrlReports._0x05) &&
                //WaitCtrlDevice() &&
                //SetCtrlReport(CtrlReports._0x07) &&
                //WaitCtrlDevice() &&
                //SetCtrlReport(CtrlReports._0x0a) &&
                //WaitCtrlDevice() &&
                //SetCtrlReport(CtrlReports._0x0b) &&
                //WaitCtrlDevice() &&
                //SetCtrlReport(CtrlReports._0x06) &&
                //WaitCtrlDevice() &&
                //SetCtrlReport(CtrlReports._0x09) &&
                //WaitCtrlDevice() &&
                SetCtrlReport(CtrlReports._0x0d) &&
                WaitCtrlDevice()
                &&
                SetCtrlReport(CtrlReports._0x13) &&
                WaitCtrlDevice()
                ;

            //_ctrlStream?.Close();

            return result;
        }

        private bool GetCtrlReport(byte report_id)
        {
            int size = _ctrlDevice.GetMaxFeatureReportLength();
            var buf = new byte[size];
            buf[0] = report_id;
            try
            {
                _ctrlStream.GetFeature(buf);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool SetCtrlReport(byte[] reportBuffer)
        {
            try
            {
                _ctrlStream.SetFeature(reportBuffer);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool WaitCtrlDevice()
        {
            int size = _ctrlDevice.GetMaxFeatureReportLength();
            byte[] buf = new byte[size];
            buf[0] = 0x04;
            for (int i = 0; i < MaxTries; i++)
            {
                try
                {
                    _ctrlStream.GetFeature(buf);
                    if (buf[1] == 0x01)
                        return true;
                }
                catch
                {
                    return false;
                }
                Thread.Sleep(5);
            }
            return false;
        }

        private static HidDevice GetFromUsages(IEnumerable<HidDevice> devices, uint usagePage, uint usage)
        {
            foreach (var dev in devices)
            {
                try
                {
                    var raw = dev.GetRawReportDescriptor();
                    var usages = EncodedItem.DecodeItems(raw, 0, raw.Length).Where(t => t.TagForGlobal == GlobalItemTag.UsagePage);

                    if (usages.Any(g => g.ItemType == ItemType.Global && g.DataValue == usagePage))
                    {
                        if (usages.Any(l => l.ItemType == ItemType.Local && l.DataValue == usage))
                        {
                            return dev;
                        }
                    }
                }
                catch
                {
                    //failed to get the report descriptor, skip
                }
            }
            return null;
        }

        #endregion

        #region IDisposable Support
        /// <summary>
        /// Disconnects the keyboard when disposing
        /// </summary>
        public void Dispose() => Disconnect();
        #endregion
    }
}
