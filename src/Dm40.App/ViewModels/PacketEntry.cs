using System.Windows.Media;

namespace Dm40.App.ViewModels
{
    /// <summary>原始报文查看器的一行（时间/方向/HEX/CRC 状态）。</summary>
    public sealed class PacketEntry
    {
        public string Time { get; set; }
        public string Direction { get; set; }       // "RX" / "TX"
        public string Hex { get; set; }
        public bool CrcOk { get; set; }

        public string CrcLabel => CrcOk ? "✓ 通过" : "✗ 失败";

        public Brush DirectionBrush => Direction == "TX"
            ? new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8))   // sky
            : new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99));  // emerald RX

        public Brush CrcBrush => CrcOk
            ? new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99))
            : new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
    }
}
