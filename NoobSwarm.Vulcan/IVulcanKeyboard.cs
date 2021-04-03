using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;

namespace Vulcan.NET
{
    public interface IVulcanKeyboard 
    {
        byte Brightness { get; set; }

        event EventHandler<VolumeKnDirectionArgs> DPITurnedReceived;
        event EventHandler<KeyPressedArgs> KeyPressedReceived;
        event EventHandler<TestArgs> TestKeyPressedReceived;
        event EventHandler<VolumeKnobFxArgs> VolumeKnobFxPressedReceived;
        event EventHandler<VolumeKnobArgs> VolumeKnobPressedReceived;
        event EventHandler<VolumeKnDirectionArgs> VolumeKnobTurnedReceived;

        bool Connect();
        void Disconnect();
        byte[] GetLastSendColorsCopy();
        void SetColor(Color clr);
        void SetColors(Dictionary<int, Color> keyColors);
        void SetColors(Dictionary<LedKey, Color> keyColors);
        bool SetColors(ReadOnlySpan<byte> keyColors);
        void SetKeyColor(int key, Color clr);
        void SetKeyColor(LedKey key, Color clr);
        Task<bool> Update();
    }
}