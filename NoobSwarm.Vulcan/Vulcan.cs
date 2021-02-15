using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using NoobSwarm.Hid;
using NoobSwarm.Hid.Reports;
using NoobSwarm.Hid.Reports.Encodings;
using NoobSwarm.Hid.Reports.Input;

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
        public KeyPressedArgs(LedKey key, bool isPressed)
        {
            Key = key;
            IsPressed = isPressed;
        }

        public LedKey Key { get; set; }
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
        public event EventHandler<VolumeKnDirectionArgs> DPITurnedReceived;

        public byte Brightness
        {
            get => brightness; set
            {
                if (value == brightness)
                    return;
                brightness = Math.Clamp((byte)value, (byte)0, (byte)69);
                _ = Update().GetAwaiter().GetResult();
            }
        }

        private byte brightness = 69;

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
        private readonly AutoResetEvent updateOperationResetEvent = new(false);

        private UpdateOperation updateOperation;

        private VulcanKeyboard(HidDevice ledDevice, HidStream ledStream, HidDevice ctrlDevice, HidStream ctrlStream)
        {
            _ledDevice = ledDevice;
            _ledStream = ledStream;
            _ctrlDevice = ctrlDevice;
            _ctrlStream = ctrlStream;
            _source = new CancellationTokenSource();

            var updateOperationThread = new Thread(() => UpdateThread(_source.Token))
            {
                IsBackground = true
            };
            updateOperationThread.Start();
        }

        private static readonly byte[] normal_key_header = new byte[] { 0x03, 0x00, 0xFB };
        private static readonly byte[] easyshift_key_header = new byte[] { 0x03, 0x00, 0x0A };
        private static readonly byte[] easyshift2_key_header = new byte[] { 0x03, 0x00, 0xFD };
        private static readonly byte[] volumneknob1_key_header = new byte[] { 0x03, 0x00, 0x0B };
        private static readonly byte[] volumneknob2_key_header = new byte[] { 0x03, 0x00, 0xCC };
        private static readonly byte[] volumneknobfx_key_header = new byte[] { 0x03, 0x00, 0x0C };
        private static readonly byte[] volumneknobdpi_key_header = new byte[] { 0x03, 0x00, 0xCA };

        private static readonly Dictionary<byte, LedKey> KeyToKeyDataMapping = new Dictionary<byte, LedKey>()
        {
            { 0x11, LedKey.ESC },
            { 0x12, LedKey.TILDE },
            { 0x14, LedKey.TAB },
            { 0x16, LedKey.LEFT_SHIFT },
            { 0x17, LedKey.LEFT_CONTROL },
            { 0x13, LedKey.D1 },
            { 0x1A, LedKey.Q },
            { 0x1C, LedKey.A },
            { 29, LedKey.ISO_BACKSLASH },
            { 31, LedKey.LEFT_WINDOWS },
            { 16, LedKey.F1 },
            { 25, LedKey.D2 },
            { 27, LedKey.W },
            { 37, LedKey.S },
            { 59, LedKey.Z },
            { 39, LedKey.LEFT_ALT },
            { 24, LedKey.F2 },
            { 34, LedKey.D3 },
            { 36, LedKey.E },
            { 44, LedKey.D },
            { 45, LedKey.X },
            { 33, LedKey.F3 },
            { 35, LedKey.D4 },
            { 43, LedKey.R },
            { 53, LedKey.F },
            { 46, LedKey.C },
            { 32, LedKey.F4 },
            { 42, LedKey.D5 },
            { 51, LedKey.T },
            { 52, LedKey.G },
            { 54, LedKey.V },
            { 41, LedKey.D6 },
            { 38, LedKey.Y },
            { 61, LedKey.H },
            { 62, LedKey.B },
            { 63, LedKey.SPACE },
            { 40, LedKey.F5 },
            { 49, LedKey.D7 },
            { 60, LedKey.U },
            { 68, LedKey.J },
            { 71, LedKey.N },
            { 48, LedKey.F6 },
            { 66, LedKey.D8 },
            { 67, LedKey.I },
            { 69, LedKey.K },
            { 70, LedKey.M },
            { 56, LedKey.F7 },
            { 65, LedKey.D9 },
            { 76, LedKey.O },
            { 77, LedKey.L },
            { 78, LedKey.OEMCOMMA },
            { 57, LedKey.F8 },
            { 74, LedKey.D0 },
            { 84, LedKey.P },
            { 85, LedKey.SEMICOLON },
            { 86, LedKey.OEMPERIOD },
            { 103, LedKey.RIGHT_ALT },
            { 75, LedKey.OEMMINUS },
            { 91, LedKey.OPEN_BRACKET },
            { 93, LedKey.APOSTROPHE },
            { 94, LedKey.FORWARD_SLASH },
            { 119, LedKey.FN_Key },
            { 64, LedKey.F9 },
            { 83, LedKey.EQUALS },
            { 92, LedKey.CLOSE_BRACKET },
            //{ 100, Key.BACKSLASH }, // ANSI only
            { 110, LedKey.RIGHT_SHIFT },
            { 127, LedKey.APPLICATION_SELECT },
            { 72, LedKey.F10 },
            { 80, LedKey.F11 },
            { 81, LedKey.F12 },
            { 73, LedKey.BACKSPACE },
            { 107, LedKey.ENTER },
            { 135, LedKey.RIGHT_CONTROL },
            { 100, LedKey.ISO_HASH },
            { 88, LedKey.PRINT_SCREEN },
            { 89, LedKey.INSERT },
            { 90, LedKey.DELETE },
            { 109, LedKey.ARROW_LEFT },
            { 96, LedKey.SCROLL_LOCK },
            { 97, LedKey.HOME },
            { 98, LedKey.END },
            { 108, LedKey.ARROW_UP },
            { 117, LedKey.ARROW_DOWN },
            { 104, LedKey.PAUSE_BREAK },
            { 105, LedKey.PAGE_UP },
            { 106, LedKey.PAGE_DOWN },
            { 125, LedKey.ARROW_RIGHT },
            { 113, LedKey.NUM_LOCK },
            { 114, LedKey.NUMPAD7 },
            { 115, LedKey.NUMPAD4 },
            { 116, LedKey.NUMPAD1 },
            { 133, LedKey.NUMPAD0 },
            { 121, LedKey.DIVIDE },
            { 122, LedKey.NUMPAD8 },
            { 123, LedKey.NUMPAD5 },
            { 124, LedKey.NUMPAD2 },
            { 129, LedKey.MULTIPLY },
            { 130, LedKey.NUMPAD9 },
            { 131, LedKey.NUMPAD6 },
            { 132, LedKey.NUMPAD3 },
            { 141, LedKey.DECIMAL },
            { 137, LedKey.SUBTRACT },
            { 138, LedKey.ADD },
            { 140, LedKey.NUM_ENTER },
        };

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
            var memory = new ReadOnlySpan<byte>(b.Bytes, b.Offset, b.Count);

            while (true)
            {
                if (offset + 4 > memory.Length)
                    return;

                var bytes = memory[offset..(offset + 5)];
                if (bytes[0] + bytes[1] + bytes[2] + bytes[3] + bytes[4] == 0)
                {
                    return;
                }

                if ((bytes[1] + bytes[4] == 6 && bytes[3] == 255))
                {
                    offset += 5;
                    continue;
                }
                //Console.WriteLine($"Recvd {DateTime.Now:HH:mm:ss}: " + string.Join(" ", bytes.ToArray().Select(x => x.ToString("X2"))));


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
                        KeyPressedReceived?.Invoke(this, new KeyPressedArgs(LedKey.CAPS_LOCK, !down));
                    }
                    else if (bytes[0..(volumneknob1_key_header.Length)].SequenceEqual(volumneknob1_key_header))
                    {
                        VolumeKnobPressedReceived?.Invoke(this, new VolumeKnobArgs(key, down));
                    }
                    else if (bytes[0..(volumneknobfx_key_header.Length)].SequenceEqual(volumneknobfx_key_header))
                    {
                        VolumeKnobFxPressedReceived?.Invoke(this, new VolumeKnobFxArgs(key, data));
                    }
                    else if (bytes[0..(volumneknob2_key_header.Length)].SequenceEqual(volumneknob2_key_header))
                    {
                        VolumeKnobTurnedReceived?.Invoke(this, new VolumeKnDirectionArgs(key, data < 128));
                    }
                    else if (bytes[0..(volumneknobdpi_key_header.Length)].SequenceEqual(volumneknobdpi_key_header))
                    {
                        DPITurnedReceived?.Invoke(this, new VolumeKnDirectionArgs(key, data < 128));
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
                    //Console.WriteLine($"Recvd {DateTime.Now:HH:mm:ss}: " + string.Join(" ", bytes.ToArray().Select(x => x.ToString("X2"))));
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

        public bool SetColors(ReadOnlySpan<byte> keyColors)
        {
            if (keyColors.Length != _keyColors.Length)
                return false;

            for (int i = 0; i < keyColors.Length; i++)
            {
                _keyColors[i] = keyColors[i];
            }
            return true;
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

        public byte[] GetLastSendColorsCopy()
        {
            return _keyColors.ToArray();
        }

        private class UpdateOperation : IDisposable
        {
            private readonly VulcanKeyboard keyboard;
            private readonly byte[] brightnessAdjusted;
            private readonly SemaphoreSlim updateDone;
            private bool success;

            public UpdateOperation(VulcanKeyboard keyboard, byte[] brightnessAdjusted)
            {
                this.keyboard = keyboard;
                this.brightnessAdjusted = brightnessAdjusted;
                this.updateDone = new SemaphoreSlim(0);
            }

            public void Dispose()
            {
                updateDone.Dispose();
            }

            public void DoUpdate()
            {
                success = keyboard.WriteColorBuffer(brightnessAdjusted);
                updateDone.Release();
            }

            public async Task<bool> WaitAsync()
            {
                await updateDone.WaitAsync();
                return success;
            }
        }

        private UpdateOperation EnqueueUpdate(byte[] brightnessAdjusted)
        {
            var updateOperation = new UpdateOperation(this, brightnessAdjusted);
            Interlocked.Exchange(ref this.updateOperation, updateOperation)?.Dispose();
            updateOperationResetEvent.Set();
            return updateOperation;
        }

        private void UpdateThread(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (!updateOperationResetEvent.WaitOne())
                    continue;

                var updateItem = Interlocked.Exchange(ref updateOperation, null);

                updateItem?.DoUpdate();
                Thread.Sleep(16);
            }
        }

        /// <summary>
        /// Writes data to the keyboard
        /// </summary>
        public async Task<bool> Update()
        {
            var adjusted = CreateBrightnessAdjustedBuffer();
            var workItem = EnqueueUpdate(adjusted);
            return await workItem.WaitAsync().ContinueWith((passthrough) =>
            {
                workItem.Dispose();
                return passthrough.Result;
            });
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
        private byte[] CreateBrightnessAdjustedBuffer()
        {
            byte[] brightnessAdjustedBuffer;

            if (brightness == 69)
            {
                brightnessAdjustedBuffer = _keyColors.ToArray();
            }
            else
            {
                brightnessAdjustedBuffer = new byte[_keyColors.Length];
                for (int i = 0; i < brightnessAdjustedBuffer.Length; i++)
                {
                    brightnessAdjustedBuffer[i] = (byte)(_keyColors[i] / 69.0 * brightness);
                }
            }
            return brightnessAdjustedBuffer;
        }

        private bool WriteColorBuffer(byte[] brightnessAdjustedBuffer)
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
            Array.Copy(brightnessAdjustedBuffer, 0,
                        packet, ColorPacketHeader.Length,
                        65 - ColorPacketHeader.Length);//copy the first 60 bytes of color data to the packet
                                                       //so 60 data + 5 header fits in a packet
            try
            {

                for (int i = 1; i <= 6; i++)//each chunk consists of the byte 0x00 and 64 bytes of data after that
                {
                    Array.Copy(brightnessAdjustedBuffer, (i * 64) - 4,//each packet holds 64 except for the first one, hence we subtract 4
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
