using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

using HidSharp;
using HidSharp.Reports.Encodings;
using HidSharp.Reports.Input;

namespace Vulcan.NET
{
    /// <summary>
    /// Class representing a vulcan Keyboard. Can only interface with one at a time
    /// </summary>
    public sealed class VulcanKeyboard : IDisposable
    {
        public event EventHandler<ByteEventArgs> KeyPressedReceived;

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
                                    Console.WriteLine("Recvd: " + string.Join(" ", b.Bytes.Take(10).Select(x => x.ToString("X2"))));
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
