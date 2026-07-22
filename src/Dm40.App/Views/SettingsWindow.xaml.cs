using System;
using System.Globalization;
using System.IO;
using System.Windows;
using Dm40.App.ViewModels;

namespace Dm40.App.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly MainViewModel _vm;

        public SettingsWindow(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;

            SetWindowRadio(vm.WindowSeconds);
            AutoConnectBox.IsChecked = vm.AutoConnect;
            ShotDirBox.Text = vm.ScreenshotDir;
        }

        private void OnBrowseShotDir(object sender, RoutedEventArgs e)
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择截图保存目录",
                SelectedPath = Directory.Exists(ShotDirBox.Text) ? ShotDirBox.Text : "",
                ShowNewFolderButton = true,
            })
            {
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    ShotDirBox.Text = dlg.SelectedPath;
            }
        }

        // 回显时间窗口：命中 30/60/300/600 则选对应快捷项，否则选「自定义」并填入秒数
        private void SetWindowRadio(int secs)
        {
            Win30.IsChecked = secs == 30;
            Win60.IsChecked = secs == 60;
            Win300.IsChecked = secs == 300;
            Win600.IsChecked = secs == 600;
            bool custom = !(Win30.IsChecked == true || Win60.IsChecked == true || Win300.IsChecked == true || Win600.IsChecked == true);
            WinCustom.IsChecked = custom;
            if (custom) CustomWinBox.Text = secs.ToString(CultureInfo.InvariantCulture);
            CustomWinBox.IsEnabled = custom;
        }

        private void OnWinChecked(object sender, RoutedEventArgs e)
        {
            if (CustomWinBox == null) return;
            CustomWinBox.IsEnabled = WinCustom.IsChecked == true;
        }

        private int SelectedWindowSeconds()
        {
            if (Win30.IsChecked == true) return 30;
            if (Win300.IsChecked == true) return 300;
            if (Win600.IsChecked == true) return 600;
            return 60;
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            int winSec;
            if (WinCustom.IsChecked == true)
            {
                if (!int.TryParse(CustomWinBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out winSec) || winSec < 5)
                {
                    MessageBox.Show(this, "自定义秒数需为不小于 5 的整数。", "校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            else winSec = SelectedWindowSeconds();

            _vm.WindowSeconds = winSec;
            _vm.AutoConnect = AutoConnectBox.IsChecked == true;
            var dir = (ShotDirBox.Text ?? "").Trim();
            if (dir.Length > 0) _vm.ScreenshotDir = dir;
            DialogResult = true;
            Close();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
