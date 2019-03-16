// Copyleft freakbyte 2015, feel free to do whatever you want with this class.

using System;
using System.Globalization;

namespace FauFau.Util
{
    public class Color
    {
        #region Variables
        private bool _uintDirty = false;
        private bool _rgbaDirty = true;
        private bool _bytesDirty = true;
        private bool _hexDirty = true;

        private uint _uint = 0;
        private float[] _rgba;
        private byte[] _bytes;
        private string _hex;
        #endregion

        #region Values
        public uint UInt
        {
            get
            {
                if (_uintDirty)
                {
                    if (!_bytesDirty)
                    {
                        _uint = BitConverter.ToUInt32(_bytes, 0);
                    }
                    else if (!_rgbaDirty)
                    {
                        _bytes = new byte[] 
                        {
                            (byte)(_rgba[0] * 255),
                            (byte)(_rgba[1] * 255),
                            (byte)(_rgba[2] * 255),
                            (byte)(_rgba[3] * 255)
                        };
                        _bytesDirty = false;

                        _uint = BitConverter.ToUInt32(_bytes, 0);
                    }
                    else
                    {
                        _uint = Convert.ToUInt32(_hex, 16);
                    }
                    _uintDirty = false;
                }
                return _uint;
            }
            set
            {
                _uint = value;
                _uintDirty = false;
                _rgbaDirty = true;
                _bytesDirty = true;
                _hexDirty = true;
            }
        }
        public float[] RGBA
        {
            get
            {
                if (_rgbaDirty)
                {
                    if (!_uintDirty)
                    {
                        _bytes = BitConverter.GetBytes(_uint);
                        _bytesDirty = false;
                        _rgba = new float[] { _bytes[0] / 255.0f, _bytes[1] / 255.0f, _bytes[2] / 255.0f, _bytes[3] / 255.0f };
                    }
                    else if (!_bytesDirty)
                    {
                        _rgba = new float[] { _bytes[0] / 255.0f, _bytes[1] / 255.0f, _bytes[2] / 255.0f, _bytes[3] / 255.0f };
                    }
                    else
                    {
                        _uint = Convert.ToUInt32(_hex, 16);
                        _uintDirty = false;
                        _bytes = BitConverter.GetBytes(_uint);
                        _bytesDirty = false;
                        _rgba = new float[] { _bytes[0] / 255.0f, _bytes[1] / 255.0f, _bytes[2] / 255.0f, _bytes[3] / 255.0f };
                    }
                    _rgbaDirty = false;
                }
                return _rgba;
            }
            set
            {
                if (value.Length == 4)
                {
                    _rgba = new float[]
                    {
                        Math.Clamp(value[0], 0.0f, 1.0f),
                        Math.Clamp(value[1], 0.0f, 1.0f),
                        Math.Clamp(value[2], 0.0f, 1.0f),
                        Math.Clamp(value[3], 0.0f, 1.0f)
                    };
                    _uintDirty = true;
                    _rgbaDirty = false;
                    _bytesDirty = true;
                    _hexDirty = true;
                }
            }
        }
        public float[] Normalized
        {
            get
            {
                float[] ret = RGBA;
                ret[0] = (ret[0] * 2.0f) - 1.0f;
                ret[1] = (ret[0] * 2.0f) - 1.0f;
                ret[2] = (ret[0] * 2.0f) - 1.0f;
                ret[3] = (ret[0] * 2.0f) - 1.0f;
                return ret;
            }
            set
            {
                if (value.Length == 4)
                {
                    float[] _in = new float[] 
                    {
                        Math.Clamp(value[0], -1.0f, 1.0f),
                        Math.Clamp(value[1], -1.0f, 1.0f),
                        Math.Clamp(value[2], -1.0f, 1.0f),
                        Math.Clamp(value[3], -1.0f, 1.0f)
                    };
                    _in[0] = (_in[0] + 1.0f) / 2.0f;
                    _in[1] = (_in[0] + 1.0f) / 2.0f;
                    _in[2] = (_in[0] + 1.0f) / 2.0f;
                    _in[3] = (_in[0] + 1.0f) / 2.0f;
                    RGBA = _in;
                }
            }
        }
        public byte[] Bytes
        {
            get
            {
                if (_bytesDirty)
                {
                    if (!_uintDirty)
                    {
                        _bytes = BitConverter.GetBytes(_uint);
                    }
                    else if (!_rgbaDirty)
                    {
                        _bytes = new byte[]
                        {
                            (byte)(_rgba[0] * 255),
                            (byte)(_rgba[1] * 255),
                            (byte)(_rgba[2] * 255),
                            (byte)(_rgba[3] * 255)
                        };
                    }
                    else
                    {
                        _uint = Convert.ToUInt32(_hex, 16);
                        _uintDirty = false;
                        _bytes = BitConverter.GetBytes(_uint);
                    }
                    _bytesDirty = false;
                }
                return _bytes;
            }
            set
            {
                if (value.Length == 4)
                {
                    _bytes = value;
                    _uintDirty = true;
                    _rgbaDirty = true;
                    _bytesDirty = false;
                    _hexDirty = true;
                }
            }
        }
        public string Hex
        {
            get
            {
                if (_hexDirty)
                {
                    if (!_bytesDirty)
                    {
                        _hex = "0x" + BitConverter.ToString(_bytes).Replace("-", string.Empty);
                    }
                    else if (!_uintDirty)
                    {
                        _bytes = BitConverter.GetBytes(_uint);
                        _bytesDirty = false;
                        _hex = "0x" + BitConverter.ToString(_bytes).Replace("-", string.Empty);
                    }
                    else
                    {
                        _bytes = new byte[]
                        {
                            (byte)(_rgba[0] * 255),
                            (byte)(_rgba[1] * 255),
                            (byte)(_rgba[2] * 255),
                            (byte)(_rgba[3] * 255)
                        };
                        _bytesDirty = false;
                        _hex = "0x" + BitConverter.ToString(_bytes).Replace("-", string.Empty);
                    }
                    _hexDirty = false;
                }
                return _hex;
            }
            set
            {
                uint output;
                if (value.Length == 10 && value.StartsWith("0x") && UInt32.TryParse(value.Substring(2), NumberStyles.HexNumber, null, out output))
                {
                    _hex = value;
                    _uintDirty = true;
                    _rgbaDirty = true;
                    _bytesDirty = true;
                    _hexDirty = false;
                }
            }
        }
        #endregion
    }
}
