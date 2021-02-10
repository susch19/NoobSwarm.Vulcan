using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using HidSharp;
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
        public VolumeKnobArgs(byte id, byte data)
        {
            Id = id;
            Data = data;
        }

        public byte Data { get; set; }
        public byte Id { get; set; }
    }

    /// <summary>
    /// Class representing a vulcan Keyboard. Can only interface with one at a time
    /// </summary>
    public sealed class VulcanKeyboard : IDisposable
    {
        public event EventHandler<ByteEventArgs> KeyPressedReceived;
        public event EventHandler<TestArgs> TestKeyPressedReceived;
        public event EventHandler<KeyPressedArgs> FinalKeyPressedReceived;
        public event EventHandler<VolumeKnobArgs> FinalVolumeKnobPressedReceived;


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
        private static readonly List<HidStream> streamsToDispose = new List<HidStream>();

        private VulcanKeyboard(HidDevice ledDevice, HidStream ledStream, HidDevice ctrlDevice, HidStream ctrlStream, HidDevice inputDevice, HidStream inputStream, HidDeviceInputReceiver receiver)
        {
            _ledDevice = ledDevice;
            _ledStream = ledStream;
            _ctrlDevice = ctrlDevice;
            _ctrlStream = ctrlStream;
            _inputDevice = inputDevice;
            _inputStream = inputStream;
            _source = new CancellationTokenSource();
            _receiver = receiver;
            _receiver.Received += _receiver_Received;
        }

        private void _receiver_Received(object sender, EventArgs e)
        {
        }


        private static readonly byte[] normal_key_header = new byte[] { 0x03, 0x00, 0xFB };
        private static readonly byte[] easyshift_key_header = new byte[] { 0x03, 0x00, 0x0A };
        private static readonly byte[] volumneknob1_key_header = new byte[] { 0x03, 0x00, 0x0B };
        private static readonly byte[] volumneknob2_key_header = new byte[] { 0x03, 0x00, 0xCC };

        private static readonly Dictionary<byte, Key> KeyToKeyDataMapping = new Dictionary<byte, Key>()
        {
            { 17, Key.ESC },
            { 18, Key.TILDE },
            { 20, Key.TAB },
            { 22, Key.LEFT_SHIFT },
            { 23, Key.LEFT_CONTROL },
            { 19, Key.D1 },
            { 26, Key.Q },
            { 28, Key.A },
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
                if ((ctrlDevice?.TryOpen(out ctrlStream) ?? false) && (ledDevice?.TryOpen(out ledStream) ?? false))
                {
                    var data = inputDevices.First().GetReportDescriptor();
                    var receiver = data.CreateHidDeviceInputReceiver();
                    VulcanKeyboard kb = new VulcanKeyboard(ledDevice, ledStream, ctrlDevice, ctrlStream, inputDevices.First(), inputStream, receiver);
                    if (kb.SendCtrlInitSequence())
                    {
                        foreach (var item in inputDevices)
                        {

                            var data1 = item.GetReportDescriptor();
                            var receiver1 = data.CreateHidDeviceInputReceiver();
                            receiver1.Received += (a, b) =>
                            {
                                int offset = 0;

                                while (true)
                                {

                                    if ((b.Bytes[1 + offset] + b.Bytes[4 + offset] == 6 && b.Bytes[3 + offset] == 255))
                                    {
                                        offset += 5;
                                        continue;
                                    }
                                    if (b.Bytes.Length < offset + 5 || b.Bytes[0 + offset] + b.Bytes[1 + offset] + b.Bytes[2 + offset] + b.Bytes[3 + offset] + b.Bytes[4 + offset] == 0)
                                        return;

                                    kb.KeyPressedReceived?.Invoke(a, b);
                                    //Console.WriteLine("Recvd: " + string.Join(" ", b.Bytes.Take(10).Select(x => x.ToString("X2"))));

                                    if (b.Bytes.Length > 3)
                                    {
                                        var header_ = b.Bytes.Take(3);
                                        var bb = b.Bytes.Skip(3).ToArray();
                                        var data_ = TrimEnd(bb);
                                        var key_ = bb[0];
                                        var down = b.Bytes.Skip(4).First() == 1;

                                        if (b.Bytes.Take(normal_key_header.Length).SequenceEqual(normal_key_header))
                                        {
                                            kb.FinalKeyPressedReceived?.Invoke(kb, new KeyPressedArgs(KeyToKeyDataMapping[key_], down));
                                        }
                                        else if (b.Bytes.Take(easyshift_key_header.Length).SequenceEqual(easyshift_key_header))
                                        {
                                            kb.FinalKeyPressedReceived?.Invoke(kb, new KeyPressedArgs(Key.CAPS_LOCK, down));
                                        }
                                        else if (b.Bytes.Take(volumneknob1_key_header.Length).SequenceEqual(volumneknob1_key_header))
                                        {
                                            kb.FinalVolumeKnobPressedReceived?.Invoke(kb, new VolumeKnobArgs(key_, data_.Last()));
                                            //Console.WriteLine($"knop: {key_:X2} -> \t\t" + string.Join(" ", data_.Select(x => $"{x:X2}")));
                                        }

                                        kb.TestKeyPressedReceived?.Invoke(kb, new TestArgs(key_, down));
                                        //if (header_.SequenceEqual(normal_key_header))
                                        //{
                                        //    Console.WriteLine($"Key {(down ? "down" : "up  ")}: {key_:X2} -> \t\t" + string.Join(" ", data_.Select(x => $"{x:X2}")));
                                        //}
                                        //else if (header_.SequenceEqual(volumneknob1_key_header))
                                        //{
                                        //    Console.WriteLine($"knop {(down ? "down" : "up  ")}: {key_:X2} -> \t\t" + string.Join(" ", data_.Select(x => $"{x:X2}")));
                                        //}
                                    }

                                    offset += 5;
                                }

                            };
                            if (item.TryOpen(out var str))
                            {
                                receiver1.Start(str);
                                streamsToDispose.Add(str);
                            }
                            else
                                ;

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
            foreach (Key key in (Key[])Enum.GetValues(typeof(Key)))
                SetKeyColor(key, clr);
        }

        /// <summary>
        /// Set the colors of all the keys in the dictionary
        /// </summary>
        public void SetColors(Dictionary<Key, Color> keyColors)
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
        public void SetKeyColor(Key key, Color clr)
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

            _ctrlStream?.Close();

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
