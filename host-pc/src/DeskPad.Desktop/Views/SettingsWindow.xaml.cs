using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using DeskPad.Core.Models;
using DeskPad.Core.Monitors;
using DeskPad.Desktop.Services;

namespace DeskPad.Desktop.Views;

public partial class SettingsWindow : Window
{
    public AppSettings Settings { get; }

    public SettingsWindow(AppSettings current)
    {
        InitializeComponent();
        Settings = current;

        chkAuto.IsChecked = current.AutoResolution;
        txtWidth.Text = current.TargetWidth.ToString();
        txtHeight.Text = current.TargetHeight.ToString();
        txtBitrate.Text = current.BitrateKbps.ToString();
        chkMinimized.IsChecked = current.StartMinimized;
        chkHide.IsChecked = current.HideVirtualDisplayWhenIdle;
        chkAutoStart.IsChecked = AutoStartService.IsAutoStartEnabled();

        SelectFps(current.TargetFps);
        PopulateMonitors(current.CaptureMonitorDevice);
        UpdateResolutionEnabled();

        chkAuto.Checked += (_, _) => UpdateResolutionEnabled();
        chkAuto.Unchecked += (_, _) => UpdateResolutionEnabled();
    }

    private void PopulateMonitors(string? selectedDevice)
    {
        cmbMonitor.Items.Clear();
        var monitors = DisplayController.GetMonitors();
        ComboBoxItem? toSelect = null;

        foreach (var m in monitors)
        {
            var item = new ComboBoxItem { Content = m.ToString(), Tag = m.DeviceName };
            cmbMonitor.Items.Add(item);

            if (!string.IsNullOrEmpty(selectedDevice) && m.DeviceName == selectedDevice)
                toSelect = item;
        }

        // Secim yoksa: otomatik olarak birincil olmayan (sanal) ekrani oner
        if (toSelect == null)
        {
            for (int i = 0; i < cmbMonitor.Items.Count; i++)
            {
                var item = (ComboBoxItem)cmbMonitor.Items[i];
                var m = monitors[i];
                if (!m.IsPrimary) { toSelect = item; break; }
            }
        }

        toSelect ??= cmbMonitor.Items.Count > 0 ? (ComboBoxItem)cmbMonitor.Items[0] : null;
        cmbMonitor.SelectedItem = toSelect;
    }

    private void SelectFps(int fps)
    {
        foreach (var item in cmbFps.Items)
        {
            if (item is ComboBoxItem ci && ci.Content?.ToString() == fps.ToString())
            {
                cmbFps.SelectedItem = ci;
                return;
            }
        }
        cmbFps.SelectedIndex = cmbFps.Items.Count - 1;
    }

    private void UpdateResolutionEnabled()
    {
        bool manual = chkAuto.IsChecked != true;
        txtWidth.IsEnabled = manual;
        txtHeight.IsEnabled = manual;
    }

    private void OnRotate(object sender, RoutedEventArgs e)
    {
        if (cmbMonitor.SelectedItem is not ComboBoxItem item || item.Tag is not string device)
            return;

        bool ok = DisplayController.Rotate90(device);
        if (!ok)
        {
            MessageBox.Show(this,
                "Ekran döndürülemedi. Windows Ekran Ayarları'ndan 'Ekran yönü' seçeneğini kullanabilirsiniz.",
                "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // Konum/boyut degistigi icin listeyi tazele
        PopulateMonitors(Settings.CaptureMonitorDevice);
    }

    private void OnOpenDisplaySettings(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = "ms-settings:display", UseShellExecute = true });
        }
        catch
        {
            try { Process.Start("control", "desk.cpl"); } catch { }
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        Settings.AutoResolution = chkAuto.IsChecked == true;
        if (int.TryParse(txtWidth.Text, out int w)) Settings.TargetWidth = w;
        if (int.TryParse(txtHeight.Text, out int h)) Settings.TargetHeight = h;
        if (int.TryParse(txtBitrate.Text, out int b)) Settings.BitrateKbps = b;
        if (cmbFps.SelectedItem is ComboBoxItem ci && int.TryParse(ci.Content?.ToString(), out int f))
            Settings.TargetFps = f;

        if (cmbMonitor.SelectedItem is ComboBoxItem mi && mi.Tag is string device)
            Settings.CaptureMonitorDevice = device;

        Settings.StartMinimized = chkMinimized.IsChecked == true;
        Settings.HideVirtualDisplayWhenIdle = chkHide.IsChecked == true;

        AutoStartService.SetAutoStart(chkAutoStart.IsChecked == true);
        SettingsStore.Save(Settings);

        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
