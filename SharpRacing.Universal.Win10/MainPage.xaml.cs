using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.WiFi;
using Windows.Foundation.Metadata;
using Windows.Networking;
using Windows.Networking.Connectivity;
using Windows.Networking.Sockets;
using Windows.System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;

namespace SharpRacing.Universal.Win10
{
    public sealed partial class MainPage : Page
    {
        private const string DefaultHost = "192.168.4.1"; //127.0.0.1

        private const string Port = "8077";

        private int _elasticDirectionTimerIntervalMilliseconds = 10;

        private int _controlIntervalMilliseconds = 0;

        private Connection _connection = new Connection();

        private Timer _controlTimer;

        private Stopwatch _ackCounterStopwatch = new Stopwatch();

        private Int32 _lastSecAckCounter = 0;

        private byte[] _controlPacket = new byte[4];

        private byte[] _setupPacket = new byte[74];

        private SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1);

        private byte _enabledValue;

        private byte _liftValue;

        private byte _propulsionValue;

        private byte _directionValue;

        private SetupDialog _setupDialog;

        private readonly SolidColorBrush _whiteBrush = new SolidColorBrush(Colors.White);

        private readonly SolidColorBrush _lightGreenBrush = new SolidColorBrush(Colors.LightGreen);

        private readonly SolidColorBrush _tomatoBrush = new SolidColorBrush(Colors.Tomato);

        private readonly SolidColorBrush _transparentBrush = new SolidColorBrush(Colors.Transparent);

        private bool? _lastBrushWasGreen= null;

        private StringBuilder _sbControl = new StringBuilder();

        private StringBuilder _sbSetup = new StringBuilder();

        private HovercraftRequestResult _controlRecycledResult = new HovercraftRequestResult();

        private HovercraftRequestResult _setupRecycledResult = new HovercraftRequestResult();

        private DispatcherTimer _elasticDirectionTimer;

        private double _middleDirectionValue;

        private int? _maxAdaptiveDirectionBoundsReduction;

        private double? _adaptiveDirectionRatio;

        private int? _maxAdaptiveLift;

        private double? _adaptiveLiftRatio;

        private bool _isAdaptiveLiftChecked;

        private bool _isAdaptiveDirectionBoundsChecked;

        private readonly bool _insideConstructorCode;

        private VisualStateGroup _directionCommonStates;

        private Stopwatch _lastControlStopwatch;

        private WiFiAdapter _wifiAdapter = null;

        private Uri _wifiNetworkSelectionUri;

        public MainPage()
        {
            _insideConstructorCode = true;
            try
            {
                this.InitializeComponent();

                //Since W10, where you need to check the platform at Run-Time
                _wifiNetworkSelectionUri = new Uri(ApiInformation.IsApiContractPresent("Windows.Phone.PhoneContract", 1) ?
                        //Windows Phone
                        "ms-settings:network-wifi" :
                        //Windows
                        "ms-availablenetworks:");

                _liftValue = (byte)lift.Value;
                liftText.Text = _liftValue.ToString();

                _propulsionValue = (byte)propulsion.Value;
                propulsionText.Text = _propulsionValue.ToString();

                _directionValue = (byte)direction.Value;
                directionText.Text = _directionValue.ToString();

                _setupDialog = new SetupDialog();
            }
            finally
            {
                _insideConstructorCode = false;
            }
        }

        private async void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            adaptiveLiftCheckBox.IsChecked = true;
            adaptiveDirectionBoundsCheckBox.IsChecked = true;

            ReadAdaptiveDirectionSettings();
            ReadAdaptiveLiftSettings();

            _ackCounterStopwatch.Start();
            _controlTimer = new Timer(new TimerCallback(controlTimer_Tick), null, _controlIntervalMilliseconds, Timeout.Infinite);

            _middleDirectionValue = (direction.Maximum - direction.Minimum) / 2;

            _elasticDirectionTimer = new DispatcherTimer();
            _elasticDirectionTimer.Interval = TimeSpan.FromMilliseconds(_elasticDirectionTimerIntervalMilliseconds);
            _elasticDirectionTimer.Tick += _elasticDirectionTimer_Tick;

            // Visual States are always on the first child of the control template
            var element = VisualTreeHelper.GetChild(this.direction, 0) as FrameworkElement;
            if (element != null)
            {
                _directionCommonStates = VisualStateManager.GetVisualStateGroups(element).FirstOrDefault(g => g.Name == "CommonStates");
                if (_directionCommonStates != null)
                {
                    //https://msdn.microsoft.com/fr-fr/library/windows/apps/mt299153.aspx
                    _directionCommonStates.CurrentStateChanged += _directionCommonStates_CurrentStateChanged;
                }
            }

            await FetchWiFiAdapter();
            await RefreshWiFiNetworkName();

            NetworkInformation.NetworkStatusChanged += NetworkInformation_NetworkStatusChanged;
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            NetworkInformation.NetworkStatusChanged -= NetworkInformation_NetworkStatusChanged;
        }

        private async void NetworkInformation_NetworkStatusChanged(object sender)
        {
            await RefreshWiFiNetworkName();
        }

        private async Task FetchWiFiAdapter()
        {
            _wifiAdapter = null;

            var result = await DeviceInformation.FindAllAsync(WiFiAdapter.GetDeviceSelector());
            if (result.Count >= 1)
            {
                _wifiAdapter = await WiFiAdapter.FromIdAsync(result[0].Id);
            }
        }

        private async Task RefreshWiFiNetworkName()
        {
            string wiFiName = "No WiFi !";

            if (_wifiAdapter != null)
            {
                var connectedProfile = await _wifiAdapter.NetworkAdapter.GetConnectedProfileAsync();
                if (connectedProfile != null)
                {
                    wiFiName = connectedProfile.ProfileName;
                }
            };

            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
            {
                wifiNetworkButton.Content = wiFiName;
                if (wiFiName.ToLower() != "#racing")
                {
                    wifiNetworkButton.Background = _tomatoBrush;
                    wifiNetworkButton.Foreground = _whiteBrush;
                }
                else
                {
                    wifiNetworkButton.Background = _transparentBrush;
                    wifiNetworkButton.Foreground = _lightGreenBrush;
                }
            });
        }

        private void _directionCommonStates_CurrentStateChanged(object sender, VisualStateChangedEventArgs e)
        {
            if (e.OldState.Name == "Pressed")
            {
                _elasticDirectionTimer.Stop();
                _elasticDirectionTimer.Start();
            }
        }

        private void _elasticDirectionTimer_Tick(object sender, object e)
        {
            _elasticDirectionTimer.Stop();
            direction.Value = _middleDirectionValue;
        }

        private void adaptiveLiftCheckBox_CheckedOrUnchecked(object sender, RoutedEventArgs e)
        {
            bool? isChecked = adaptiveLiftCheckBox.IsChecked;
            if (isChecked.HasValue && isChecked.Value)
            {
                _isAdaptiveLiftChecked = true;
            }
            else
            {
                _isAdaptiveLiftChecked = false;
            }
        }

        private void adaptiveDirectionBoundsCheckBox_CheckedOrUnchecked(object sender, RoutedEventArgs e)
        {
            bool? isChecked = adaptiveDirectionBoundsCheckBox.IsChecked;
            if (isChecked.HasValue && isChecked.Value)
            {
                _isAdaptiveDirectionBoundsChecked = true;

                double newDirectionMin = 0;
                double newDirectionMax = 100;
                direction.Minimum = newDirectionMin;
                direction.Maximum = newDirectionMax;

                directionMinText.Text = newDirectionMin.ToString();
                directionMaxText.Text = newDirectionMax.ToString();
            }
            else
            {
                _isAdaptiveDirectionBoundsChecked = false;
            }
        }

        private void adaptiveDirectionSettings_TextChanged(object sender, TextChangedEventArgs e)
        {
            ReadAdaptiveDirectionSettings();
        }

        private void linkPropLiftSettings_TextChanged(object sender, TextChangedEventArgs e)
        {
            ReadAdaptiveLiftSettings();
        }

        private void ReadAdaptiveLiftSettings()
        {
            string[] values = adaptiveLiftSettings.Text.Split(';');
            if (values.Length != 2)
                return;

            string maxLiftText = values[0];
            string ratioText = values[1].Replace(",", NumberFormatInfo.CurrentInfo.NumberDecimalSeparator);

            int max;
            double ratio;

            if (int.TryParse(maxLiftText, out max) && double.TryParse(ratioText, out ratio))
            {
                _maxAdaptiveLift = max;
                _adaptiveLiftRatio = ratio;
            }
            else
            {
                _maxAdaptiveLift = null;
                _adaptiveLiftRatio = null;
            }
        }

        private void ReadAdaptiveDirectionSettings()
        {
            string[] values = adaptiveDirectionSettings.Text.Split(';');
            if (values.Length != 2)
                return;

            string maxText = values[0];
            string ratioText = values[1].Replace(",", NumberFormatInfo.CurrentInfo.NumberDecimalSeparator);

            int max;
            double ratio;

            if (int.TryParse(maxText, out max) &&
                double.TryParse(ratioText, out ratio))
            {
                _maxAdaptiveDirectionBoundsReduction = max;
                _adaptiveDirectionRatio = ratio;
            }
            else
            {
                _maxAdaptiveDirectionBoundsReduction = null;
                _adaptiveDirectionRatio = null;
            }
        }

        private void propulsion_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            _propulsionValue = (byte)e.NewValue;
            propulsionText.Text = _propulsionValue.ToString();

            if (_isAdaptiveLiftChecked && _maxAdaptiveLift.HasValue && _adaptiveLiftRatio.HasValue)
            {
                lift.Value = (int) Math.Min(_maxAdaptiveLift.Value, propulsion.Value * _adaptiveLiftRatio.Value);
            }

            if (_isAdaptiveDirectionBoundsChecked && _maxAdaptiveDirectionBoundsReduction.HasValue && _adaptiveDirectionRatio.HasValue)
            {
                var boundsReduction = (int) Math.Min(_maxAdaptiveDirectionBoundsReduction.Value, propulsion.Value * _adaptiveDirectionRatio.Value);

                int newMin = 0 + boundsReduction / 2;
                int newMax = 100 - boundsReduction / 2;

                direction.Minimum = newMin;
                direction.Maximum = newMax;

                directionMinText.Text = newMin.ToString();
                directionMaxText.Text = newMax.ToString();
            }
        }

        private void lift_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            _liftValue = (byte)e.NewValue;
            liftText.Text = _liftValue.ToString();
        }

        private void direction_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            _directionValue = (byte)e.NewValue;
            directionText.Text = _directionValue.ToString();
        }

        private async void wifiNetworkButton_Click(object sender, RoutedEventArgs e)
        {
            await Launcher.LaunchUriAsync(_wifiNetworkSelectionUri);
        }

        private async void setupButton_Click(object sender, RoutedEventArgs e)
        {
            _setupDialog.SocketAddress = _connection.Host.RawName + ":" + _connection.Port;
            _setupDialog.ControlIntervalMilliseconds = _controlIntervalMilliseconds.ToString();

            if (await _setupDialog.ShowAsync() == ContentDialogResult.Secondary)
            {
                setupAckTextBlock.Text = String.Empty;
                await ApplySetup(_setupDialog.SocketAddress, _setupDialog.LiftSetupText, _setupDialog.PropSetupText, _setupDialog.ServoSetupText, _setupDialog.LiftPulseCorrections, _setupDialog.PropPulseCorrections, _setupDialog.ServoCorrection);

                int ms;
                if (int.TryParse(_setupDialog.ControlIntervalMilliseconds, out ms))
                {
                    Interlocked.Exchange(ref _controlIntervalMilliseconds, ms);
                }
            }
        }

        private void onOffToggle_CheckedOrUnchecked(object sender, RoutedEventArgs e)
        {
            bool? isChecked = this.onOffToggle.IsChecked;

            //NOTE pour eviter les accidents on remet les moteurs a zero a chaque fois qu'on demarre.
            if (!_insideConstructorCode && isChecked.HasValue && isChecked.Value)
            {
                _propulsionValue = 0;
                _liftValue = 0;

                propulsion.Value = 0;
                lift.Value = 0;
            }

            _enabledValue = isChecked.HasValue && isChecked.Value ? (byte)1 : (byte)0;
        }

        private async void controlTimer_Tick(object sender)
        {
            _lastControlStopwatch = Stopwatch.StartNew();
            while (true)
            {
                await SendControl();
                await Task.Delay(_controlIntervalMilliseconds);
            }
        }

        private async Task SendControl()
        {
            bool forceGC = false;
            try
            {
                if (!await _semaphoreSlim.WaitAsync(0))
                    return;

                HovercraftRequestResult response = null;

                string perfText = null;
                SolidColorBrush perfTextForeground = null;
                string controlAckText = null;
                string exceptionText = null;

                try
                {
                    _controlPacket[0] = _enabledValue;
                    _controlPacket[1] = _liftValue;
                    _controlPacket[2] = _propulsionValue;
                    _controlPacket[3] = _directionValue;

                    response = _controlRecycledResult;
                    _controlRecycledResult.Recycle();

                    string exceptionMessage = null;
                    try
                    {
                        await SendHovercraftData(_connection, _controlPacket, _controlRecycledResult);
                    }
                    catch (Exception ex)
                    {
                        exceptionMessage = $"{DateTimeOffset.Now} {ex.Message}" + Environment.NewLine;
                    }

                    if (!ReferenceEquals(response, null) && ReferenceEquals(response.Exception, null))
                    {
                        Interlocked.Increment(ref _lastSecAckCounter);
                        var elapsedMs = _ackCounterStopwatch.ElapsedMilliseconds;
                        if (elapsedMs >= 1000)
                        {
                            long perSec = (_lastSecAckCounter / (elapsedMs / 1000));
                            bool shouldBeGreen = perSec > 20;
                            perfText = $"{perSec}/sec";

                            if (_lastBrushWasGreen == null || _lastBrushWasGreen == shouldBeGreen)
                            {
                                perfTextForeground = shouldBeGreen ? _lightGreenBrush : _tomatoBrush;
                            }
                            _lastBrushWasGreen = shouldBeGreen;

                            controlAckText = response.AckResponse ?? String.Empty;

                            Interlocked.Exchange(ref _lastSecAckCounter, 0);
                            _ackCounterStopwatch.Restart();

                            //je tente de lisser l'exec du garbage collector pour diluer/minimiser les lags
                            forceGC = true;
                        }
                    }

                    if (ReferenceEquals(exceptionMessage, null) && !ReferenceEquals(response, null) && ReferenceEquals(response.Exception, null))
                    {
                        controlAckText = response.AckResponse ?? "(null)";
                    }
                    else
                    {
                        controlAckText = !ReferenceEquals(response, null) ? response.AckResponse ?? "(null)" : "";

                        _sbControl.Clear();
                        _sbControl.Append(exceptionMessage ?? String.Empty);
                        _sbControl.Append(!ReferenceEquals(response, null) ? (response.Exception ?? String.Empty) : String.Empty);
                        exceptionText = _sbControl.ToString();
                    }

                    bool shouldRefreshAckText = false;
                    var lastControlElapsed = _lastControlStopwatch.ElapsedMilliseconds;
                    if (lastControlElapsed > 100) //100ms = 10Hz //40ms = 25Hz
                    {
                        shouldRefreshAckText = true;
                        _lastControlStopwatch.Restart();
                    }

                    //On touche à l'UI avec parcimonie :p
                    if (exceptionText != null || shouldRefreshAckText || perfText != null)
                    {
                        await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
                        {
                            if (perfText != null)
                            {
                                perfTextBlock.Text = perfText;
                            };

                            if (perfTextForeground != null)
                            {
                                perfTextBlock.Foreground = perfTextForeground;
                            };

                            controlAckTextBlock.Text = controlAckText;
                            exceptionTextBlock.Text = exceptionText ?? "";
                        });
                    }
                }
                finally
                {
                    _semaphoreSlim.Release();
                }
            }
            finally
            {
                if (forceGC)
                {
                    GC.Collect(0, GCCollectionMode.Forced);
                }
            }
        }

        private async Task ApplySetup(
            string socketAdress, 
            string liftSetupText, 
            string propSetupText, 
            string servoSetupText, 
            string liftPulseCorrectionsText, 
            string propPulseCorrectionsText, 
            string servoCorrectionText)
        {
            var a = _setupDialog.SocketAddress.Trim().Split(':');
            if (a.Length == 2)
            {
                try
                {
                    this._connection.Host = new HostName(a[0]);
                    this._connection.Port = a[1];
                }
                catch (Exception ex)
                {
                    setupAckTextBlock.Text = String.Empty;
                    exceptionTextBlock.Text = $"{DateTimeOffset.Now} {ex.Message}";
                    return;
                }

                this._connection.Socket = null;
            }

            if (!UpdateSetupPacket(liftSetupText, propSetupText, servoSetupText, liftPulseCorrectionsText, propPulseCorrectionsText, servoCorrectionText))
                return;

            HovercraftRequestResult response = null;
            string exceptionMessage = null;

            await _semaphoreSlim.WaitAsync();
            try
            {
                response = _setupRecycledResult;
                _setupRecycledResult.Recycle();
                try
                {
                    await SendHovercraftData(_connection, _setupPacket, response);
                }
                catch (Exception ex)
                {
                    exceptionMessage = $"{DateTimeOffset.Now} {ex.Message}" + Environment.NewLine;
                }

                if (ReferenceEquals(exceptionMessage, null) && !ReferenceEquals(response, null) && ReferenceEquals(response.Exception, null))
                {
                    setupAckTextBlock.Text = response.AckResponse ?? "(null)";
                    exceptionTextBlock.Text = String.Empty;
                    return;
                }

                setupAckTextBlock.Text = !!ReferenceEquals(response, null) ? response.AckResponse ?? "(null)" : "";

                _sbSetup.Clear();
                _sbSetup.Append(exceptionMessage ?? String.Empty);
                _sbSetup.Append(!ReferenceEquals(response, null) ? (response.Exception ?? String.Empty) : String.Empty);
                exceptionTextBlock.Text = _sbSetup.ToString();
            }
            finally
            {
                _ackCounterStopwatch.Restart();
                _semaphoreSlim.Release();
            }
        }

        private async static Task SendHovercraftData(Connection connection, byte[] data, HovercraftRequestResult result)
        {
            StreamSocket socket = null;
            Stream outputStream = null;
            Stream inputStream = null;
            try
            {
                if (connection.Socket == null)
                {
                    socket = new StreamSocket();
                    connection.Socket = socket;

                    await socket.ConnectAsync(connection.Host, connection.Port);
                }
                socket = connection.Socket;

                //Write data 
                outputStream = socket.OutputStream.AsStreamForWrite();
                var writer = new BinaryWriter(outputStream);

                writer.Write(data);
                writer.Flush();

                inputStream = socket.InputStream.AsStreamForRead();
                var reader = new StreamReader(inputStream);
                result.AckResponse = await reader.ReadLineAsync();
            }
            catch (Exception e)
            {
                result.Exception = $"{DateTimeOffset.Now} {e.GetType().Name} : {e.Message}";
                if (!ReferenceEquals(socket, null))
                {
                    try
                    {
                        try
                        {
                            if (!ReferenceEquals(outputStream, null))
                            {
                                outputStream.Dispose();
                            }
                        }
                        catch (Exception outputStreamDisposeException)
                        {
                            result.Exception += $@"
{DateTimeOffset.Now} outputStream.Dispose() {outputStreamDisposeException.GetType().Name} : {outputStreamDisposeException.Message}";
                        }

                        try
                        {
                            if (!ReferenceEquals(inputStream, null))
                            {
                                inputStream.Dispose();
                            }
                        }
                        catch (Exception inputStreamDisposeException)
                        {
                            result.Exception += $@"
{DateTimeOffset.Now} inputStream.Dispose() {inputStreamDisposeException.GetType().Name} : {inputStreamDisposeException.Message}";
                        }

                        socket.Dispose();
                    }
                    catch (Exception disposeException)
                    {
                        result.Exception += $@"
{DateTimeOffset.Now} socket.Dispose() {disposeException.GetType().Name} : {disposeException.Message}";
                    }
                    finally
                    {
                        connection.Socket = null;
                    }
                }
            }
        }

        private void WriteByte(byte value, byte[] data, ref int offset)
        {
            data[offset] = value;
            offset++;
        }

        private void WriteBytes(Int16 value, byte[] data, ref int offset)
        {
            byte byte0 = (byte)((value >> 8) & 0x00FF);
            byte byte1 = (byte)(value & 0x00FF);

            // big-endian
            data[offset] = byte0;
            data[offset + 1] = byte1;

            offset = offset + 2;
        }

        private void WriteBytes(UInt16 value, byte[] data, ref int offset)
        {
            byte byte0 = (byte)((value >> 8) & 0x00FF);
            byte byte1 = (byte)(value & 0x00FF);

            // big-endian
            data[offset] = byte0;
            data[offset + 1] = byte1;

            offset = offset + 2;
        }

        private void WriteBytes(UInt32 value, byte[] data, ref int offset)
        {
            byte byte0 = (byte)((value >> 24) & 0x000000FF);
            byte byte1 = (byte)((value >> 16) & 0x000000FF);
            byte byte2 = (byte)((value >> 8) & 0x000000FF);
            byte byte3 = (byte)( value & 0x000000FF);

            // big-endian
            data[offset] = byte0;
            data[offset + 1] = byte1;
            data[offset + 2] = byte2;
            data[offset + 3] = byte3;

            offset = offset + 4;
        }

        private void WriteBytes(LedcSetupData lecdSetup, byte[] data, ref int offset)
        {
            WriteBytes(lecdSetup.freq, data, ref offset);
            WriteByte(lecdSetup.resolution_bits, data, ref offset);
            WriteBytes(lecdSetup.map_in_min, data, ref offset);
            WriteBytes(lecdSetup.map_in_max, data, ref offset);
            WriteBytes(lecdSetup.map_out_min, data, ref offset);
            WriteBytes(lecdSetup.map_out_max, data, ref offset);
        }

        private LedcSetupData ParseLedcSetupData(string text)
        {
            var fragments = text.Trim().Split(';');
            if (fragments.Length != 6)
                throw new Exception($"'{text}' : 6 fragments expected");

            UInt32 freq = UInt32.Parse(fragments[0]);
            byte resolution_bits = Byte.Parse(fragments[1]);
            UInt32 map_in_min = UInt32.Parse(fragments[2]);
            UInt32 map_in_max = UInt32.Parse(fragments[3]);
            UInt32 map_out_min = UInt32.Parse(fragments[4]);
            UInt32 map_out_max = UInt32.Parse(fragments[5]);

            return new LedcSetupData
            {
                freq = freq,
                resolution_bits = resolution_bits, 
                map_in_min = map_in_min, 
                map_in_max = map_in_max,
                map_out_min = map_out_min,
                map_out_max = map_out_max
            };
        }

        private bool UpdateSetupPacket(
            string liftSetupText, 
            string propSetupText, 
            string servoSetupText, 
            string liftPulseCorrections, 
            string propPulseCorrections, 
            string servoCorrectionText)
        {
            bool result = false;
            try
            {
                var liftSetup = ParseLedcSetupData(liftSetupText);
                var propSetup = ParseLedcSetupData(propSetupText);
                var servoSetup = ParseLedcSetupData(servoSetupText);

                var lcx = liftPulseCorrections.Trim().Split(';');
                if (lcx.Length != 2)
                    throw new ArgumentException(nameof(liftPulseCorrections));

                UInt16 lift1PulseCorrection = UInt16.Parse(lcx[0]);
                UInt16 lift2PulseCorrection = UInt16.Parse(lcx[1]);

                var pcx = propPulseCorrections.Trim().Split(';');
                if (pcx.Length != 2)
                    throw new ArgumentException(nameof(propPulseCorrections));

                UInt16 prop1PulseCorrection = UInt16.Parse(pcx[0]);
                UInt16 prop2PulseCorrection = UInt16.Parse(pcx[1]);

                Int16 servoCorrection = Int16.Parse(servoCorrectionText);

                int offset = 0;
                WriteByte(2, _setupPacket, ref offset);

                WriteBytes(liftSetup, _setupPacket, ref offset);
                WriteBytes(propSetup, _setupPacket, ref offset);
                WriteBytes(servoSetup, _setupPacket, ref offset);

                WriteBytes(lift1PulseCorrection, _setupPacket, ref offset);
                WriteBytes(lift2PulseCorrection, _setupPacket, ref offset);

                WriteBytes(prop1PulseCorrection, _setupPacket, ref offset);
                WriteBytes(prop2PulseCorrection, _setupPacket, ref offset);

                WriteBytes(servoCorrection, _setupPacket, ref offset);

                result = true;
            }
            catch (Exception ex)
            {
                setupAckTextBlock.Text = String.Empty;
                exceptionTextBlock.Text = $"{DateTimeOffset.Now} {ex.Message}";
            }

            return result;
        }

        private class HovercraftRequestResult
        {
            public string AckResponse = String.Empty;
            public string Exception = null;

            public void Recycle()
            {
                AckResponse = null;
                Exception = null;
            }
        }

        private struct LedcSetupData
        {
            public UInt32 freq;
            public byte resolution_bits;
            public UInt32 map_in_min;
            public UInt32 map_in_max;
            public UInt32 map_out_min;
            public UInt32 map_out_max;
        }

        private class Connection
        {

            public HostName Host = new HostName(MainPage.DefaultHost);

            public string Port = MainPage.Port;

            public StreamSocket Socket;
        }
    }
}