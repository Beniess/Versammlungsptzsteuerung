// Copyright 2020 Jon Skeet. All rights reserved.
// Use of this source code is governed by the Apache License 2.0,
// as found in the LICENSE.txt file.

using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Configuration;
using System.Windows.Controls;
using System.Threading.Tasks;
using System.Collections.Specialized;

namespace CameraControl.Visca.Demo
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private ViscaController controller;
        private readonly NameValueCollection testSettings;
        private NameValueCollection AppSettings => testSettings ?? ConfigurationManager.AppSettings;

        public event PropertyChangedEventHandler PropertyChanged;

        public int PanSpeed { get; private set; } = 0;
        public int TiltSpeed { get; private set; } = 0;
        public int ZoomSpeed { get; private set; } = 0;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            Connect(this, new RoutedEventArgs());
            SetUpInterface();
        }

        internal MainWindow(ViscaController controller, NameValueCollection settings)
        {
            this.controller = controller;
            testSettings = settings;
            InitializeComponent();
            DataContext = this;
            SetUpInterface();
        }

        private void Connect(object sender, RoutedEventArgs e)
        {
            controller = ViscaController.ForTcp(ConfigurationManager.AppSettings["IP_Adress"], Convert.ToInt32(ConfigurationManager.AppSettings["Port"]));
            var rtspStream = WebEye.Stream.FromUri(
                uri: new Uri("rtsp://" + ConfigurationManager.AppSettings["IP_Adress"] + ":" + Convert.ToInt32(ConfigurationManager.AppSettings["Port"]) + "/1"),
                connectionTimeout: TimeSpan.FromSeconds(10),
                streamTimeout: TimeSpan.FromSeconds(10),
                transport: WebEye.RtspTransport.Undefined,
                flags: WebEye.RtspFlags.PreferTcp);
            rtspStream.Start();
            cameraStream.Stream = rtspStream;
        }

        private void StopContinuousPanTilt(object sender, MouseButtonEventArgs e) => SetPanTiltSpeed(0, 0);
        private void StopContinuousZoom(object sender, MouseButtonEventArgs e) => SetZoomSpeed(0);

        private void AdjustContinuousPanTilt(object sender, MouseButtonEventArgs e) =>
            AdjustContinuousPanTilt(sender, (MouseEventArgs)e);

        private void AdjustContinuousPanTilt(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }
            var position = e.GetPosition(panButton);
            AdjustPanTiltPosition(position, panButton.ActualWidth, panButton.ActualHeight);
        }

        private void AdjustPanTiltPosition(Point position, double width, double height)
        {
            int panSpeed = NormalizeSpeed(position.X, width, ViscaController.MaxPanTiltSpeed);
            // Tilt has "up is positive" in camera coordinates, but "up is negative" in screen coordinates
            int tiltSpeed = -NormalizeSpeed(position.Y, height, ViscaController.MaxPanTiltSpeed);
            SetPanTiltSpeed(panSpeed, tiltSpeed);
        }

        private void AdjustContinuousZoom(object sender, MouseButtonEventArgs e) =>
            AdjustContinuousZoom(sender, (MouseEventArgs)e);

        private void AdjustContinuousZoom(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }
            var position = e.GetPosition(zoomButton);
            AdjustZoomPosition(position.X, zoomButton.ActualWidth);
        }

        private void AdjustZoomPosition(double x, double width)
        {
            int zoomSpeed = NormalizeSpeed(x, width, ViscaController.MaxZoomSpeed);
            SetZoomSpeed(zoomSpeed);
        }

        internal void TestPanTiltPosition(double x, double y, double width, double height, bool pressed)
        {
            if (pressed) AdjustPanTiltPosition(new Point(x, y), width, height);
        }

        internal void TestZoomPosition(double x, double width, bool pressed)
        {
            if (pressed) AdjustZoomPosition(x, width);
        }

        internal void TestReleasePanTilt() => StopContinuousPanTilt(this, null);
        internal void TestReleaseZoom() => StopContinuousZoom(this, null);
        internal void TestHome() => Reset(this, new RoutedEventArgs());

        private void SetPanTiltSpeed(int panSpeed, int tiltSpeed)
        {
            if (panSpeed == PanSpeed && tiltSpeed == TiltSpeed)
            {
                return;
            }
            bool? panDirection = panSpeed == 0 ? default(bool?) : panSpeed > 0;
            bool? tiltDirection = tiltSpeed == 0 ? default(bool?) : tiltSpeed > 0;
            controller?.ContinuousPanTilt(panDirection, tiltDirection, AbsSpeed(panSpeed), AbsSpeed(tiltSpeed), default);
            PanSpeed = panSpeed;
            TiltSpeed = tiltSpeed;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PanSpeed)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TiltSpeed)));
        }

        private void SetZoomSpeed(int zoomSpeed)
        {
            if (zoomSpeed == ZoomSpeed)
            {
                return;
            }
            bool? zoomDirection = zoomSpeed == 0 ? default(bool?) : zoomSpeed > 0;
            controller?.ContinuousZoom(zoomDirection, AbsSpeed(zoomSpeed), default);
            ZoomSpeed = zoomSpeed;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ZoomSpeed)));
        }

        /// <summary>
        /// Normalizes a speed to the range [-maxValue, maxValue], assuming that <paramref name="position"/>
        /// should be in the range [0, visualScale] (but might be outside that range, in which case
        /// the maximum value should be used).
        /// </summary>
        private static int NormalizeSpeed(double position, double visualScale, int maxValue)
        {
            // Scale to -1 / +1
            double scaledPosition = (position / visualScale) * 2 - 1;
            int speed = (int)(scaledPosition * (maxValue + 1));
            speed = Math.Min(speed, maxValue);
            speed = Math.Max(speed, -maxValue);
            return speed;
        }

        /// <summary>
        /// Works out the speed to use in the VISCA packet, assuming that a speed
        /// of 0 should become 1 (as other code will have set the direction to ignore the
        /// value) and negative values should become positive.
        /// </summary>
        private static byte AbsSpeed(int speed) => (byte)Math.Max(Math.Abs(speed), 1);

        private void Reset(object sender, RoutedEventArgs e) => controller?.GoHome();

        public void GoToPosition(Position position)
        {
            short pan = Convert.ToInt16(AppSettings["System.Windows.Controls.ComboBoxItem: " + position.ToString() + "Pan"]);
            short tilt = Convert.ToInt16(AppSettings["System.Windows.Controls.ComboBoxItem: " + position.ToString() + "Tilt"]);
            short zoom = Convert.ToInt16(AppSettings["System.Windows.Controls.ComboBoxItem: " + position.ToString() + "Zoom"]);
            controller?.SetPanTilt(pan, tilt, BitConverter.GetBytes(12)[0], BitConverter.GetBytes(12)[0]);
            controller?.SetZoom(zoom);
        }

        private void SetUpInterface()
        {
            foreach (Position position in (Position[])Enum.GetValues(typeof(Position))) //https://stackoverflow.com/questions/105372/how-to-enumerate-an-enum
            {
                Button button = new Button();
                button.Content = position.ToString();
                button.Click += new RoutedEventHandler((sender, e) => GoToPosition(position));
                spPositionenButtons.Children.Add(button);

                ComboBoxItem comboBoxItem = new ComboBoxItem() { Content = position.ToString() };
                cbPositionSettings.Items.Add(comboBoxItem);
            }

            tbIP.Text = AppSettings["IP_Adress"];
            tbPort.Text = AppSettings["Port"];
        }

        private void SavePositionSettings(string position)
        {
            Configuration configuration = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

            configuration.AppSettings.Settings.Remove(position.ToString() + "Pan");
            configuration.AppSettings.Settings.Remove(position.ToString() + "Tilt");               //Remove Existing App Setting as it cant be edited
            configuration.AppSettings.Settings.Remove(position.ToString() + "Zoom");

            configuration.AppSettings.Settings.Add(position.ToString() + "Pan", tbPan.Text.ToString());
            configuration.AppSettings.Settings.Add(position.ToString() + "Tilt", tbTilt.Text.ToString());     //Recreates App Setting with another value
            configuration.AppSettings.Settings.Add(position.ToString() + "zoom", tbZoom.Text.ToString());

            configuration.Save(ConfigurationSaveMode.Full, true);
            ConfigurationManager.RefreshSection("appSettings");
        }

        private void SaveNetworkSettings()
        {
            Configuration configuration = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

            configuration.AppSettings.Settings.Remove("IP_Adress");
            configuration.AppSettings.Settings.Remove("Port");               //Remove Existing App Setting as it cant be edited

            configuration.AppSettings.Settings.Add("IP_Adress", tbIP.Text);
            configuration.AppSettings.Settings.Add("Port", tbPort.Text);     //Recreates the App Setting with another value

            configuration.Save(ConfigurationSaveMode.Full, true);
            ConfigurationManager.RefreshSection("appSettings");
        }

        private void cbPositionSettings_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string position = cbPositionSettings.SelectedItem.ToString();

            tbPan.Text = AppSettings[position + "Pan"];
            tbTilt.Text = AppSettings[position + "Tilt"];
            tbZoom.Text = AppSettings[position + "Zoom"];
        }

        private void btnReconnect_Click(object sender, RoutedEventArgs e)
        {
            Connect(this, new RoutedEventArgs());
        }

        private void btnSavePositionData_Click(object sender, RoutedEventArgs e)
        {
            SavePositionSettings(cbPositionSettings.SelectedItem.ToString());
        }

        private void btnSaveNetworkSettings_Click(object sender, RoutedEventArgs e)
        {
            SaveNetworkSettings();
        }

        private void btnGetCurrentCameraPosition_Click(object sender, RoutedEventArgs e)
        {
            short pan = (short)controller?.GetPanTilt().Result.pan;
            short tilt = (short)controller?.GetPanTilt().Result.tilt;
            short zoom = (short)controller?.GetZoom().Result;

            tbPan.Text = pan.ToString();
            tbTilt.Text = tilt.ToString();
            tbZoom.Text = zoom.ToString();
        }
    }

    public enum Position
    {
        Redner,
        Leser,
        Tisch,
        Bühne,
        Wachturm
    }
}
