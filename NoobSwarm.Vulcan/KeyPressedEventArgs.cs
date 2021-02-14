using System;
using System.Collections.Generic;
using System.Text;

namespace Vulcan.NET
{
    public class KeyPressedEventArgs : EventArgs
    {
        public LedKey LedKey { get;  }


        /// <summary>
        /// The char equivalent of the pressed <see cref="Key"/>. If no key exists, for example when pressing <see cref="Key.FN_Key"/> this will be null
        /// </summary>
        public char? TextChar { get; }

        /// <summary>
        /// Indicates whether the key event was during a key down stroke or while key up. <see langword="true"/> when the key is pressed down
        /// </summary>
        public bool KeyDown { get; }
    }
}
