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
        private const string DefaultHost = "192.168.4.1"; //"127.0.0.1";

        private const string Port = "8077";

        private const int ControlPacketLength = 6;

        private const int SetupPacketLength = 72;

        private byte[] _controlPacket = new byte[ControlPacketLength];

        private byte[] _setupPacket = new byte[SetupPacketLength];

        private StringBuilder _controlAckText = new StringBuilder(512);

        private StringBuilder _setupAckText = new StringBuilder(1024);

        private int _elasticDirectionTimerIntervalMilliseconds = 10;

        private int _controlIntervalMilliseconds = 0;

        private Connection _connection = new Connection();

        private Timer _controlTimer;

        private Stopwatch _ackCounterStopwatch = new Stopwatch();

        private Int32 _lastSecAckCounter = 0;

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

        private bool _isBoostAllowed;

        private bool _isBoostPressed;

        private bool _propWhenTurningChecked;

        private byte? _propWhenTurningValue;

        private bool _isDirectionByLiftPressed;

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
            // If we have a phone contract, hide the status bar
            if (ApiInformation.IsApiContractPresent("Windows.Phone.PhoneContract", 1, 0))
            {
                var statusBar = Windows.UI.ViewManagement.StatusBar.GetForCurrentView();
                await statusBar.HideAsync();

                //var sysNavManager = Windows.UI.Core.SystemNavigationManager.GetForCurrentView();
                //sysNavManager.AppViewBackButtonVisibility = Windows.UI.Core.AppViewBackButtonVisibility.Collapsed;
            }

            _middleDirectionValue = (direction.Maximum - direction.Minimum) / 2;

            adaptiveLiftCheckBox.IsChecked = true;
            adaptiveDirectionBoundsCheckBox.IsChecked = false;
            boostEnabledCheckBox.IsChecked = true;
            propWhenTurningCheckBox.IsChecked = true;
            directionByLiftButton.IsChecked = false;

            ReadAdaptiveDirectionSettings();
            ReadAdaptiveLiftSettings();
            ReadBoostSettings();
            ReadPropWhenTurningSettings();

            _ackCounterStopwatch.Start();
            _controlTimer = new Timer(new TimerCallback(controlTimer_Tick), null, _controlIntervalMilliseconds, Timeout.Infinite);

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

        private static async Task<WiFiAdapter> FetchWiFiAdapter()
        {
            WiFiAdapter wifiAdapter = null;

            var result = await DeviceInformation.FindAllAsync(WiFiAdapter.GetDeviceSelector());
            if (result.Count >= 1)
            {
                try
                {
                    wifiAdapter = await WiFiAdapter.FromIdAsync(result[0].Id);
                }
                catch
                {
                }
            }

            return wifiAdapter;
        }

        private async Task RefreshWiFiNetworkName()
        {
            string wiFiName = "No WiFi !";

            if (_wifiAdapter == null)
            {
                _wifiAdapter = await FetchWiFiAdapter();
            }

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

                if (propWhenTurningCheckBox.IsChecked.HasValue && propWhenTurningCheckBox.IsChecked.Value)
                {
                    propWhenTurningCheckBox.IsChecked = false;
                }
            }
            else
            {
                _isAdaptiveDirectionBoundsChecked = false;
            }

            double newDirectionMin = 0;
            double newDirectionMax = 100;
            direction.Minimum = newDirectionMin;
            direction.Maximum = newDirectionMax;

            directionMinText.Text = newDirectionMin.ToString();
            directionMaxText.Text = newDirectionMax.ToString();
        }

        private void adaptiveDirectionSettings_TextChanged(object sender, TextChangedEventArgs e)
        {
            ReadAdaptiveDirectionSettings();
        }

        private void linkPropLiftSettings_TextChanged(object sender, TextChangedEventArgs e)
        {
            ReadAdaptiveLiftSettings();
        }

        private void boostEnabledCheckBox_CheckedOrUnchecked(object sender, RoutedEventArgs e)
        {
            _isBoostAllowed = boostEnabledCheckBox.IsChecked.HasValue && boostEnabledCheckBox.IsChecked.Value;
            boostButton.Visibility = _isBoostAllowed ? Visibility.Visible : Visibility.Collapsed;

            if (!_isBoostAllowed && boostButton.IsChecked.HasValue && boostButton.IsChecked.Value)
            {
                boostButton.IsChecked = false;
            }
        }

        private void ReadBoostSettings()
        {
            _isBoostAllowed = boostEnabledCheckBox.IsChecked.HasValue && boostEnabledCheckBox.IsChecked.Value;
        }

        private void boostButton_CheckedOrUnchecked(object sender, RoutedEventArgs e)
        {
            _isBoostPressed = boostButton.IsChecked.HasValue && boostButton.IsChecked.Value;

            if (_isAdaptiveLiftChecked && _maxAdaptiveLift.HasValue)
            {
                lift.Value = _isBoostPressed ? _maxAdaptiveLift.Value : 0;
                if (!_isBoostPressed)
                {
                    propulsion.Value = 0;
                }
            }
        }

        private void propWhenTurningCheckBox_CheckedOrUnchecked(object sender, RoutedEventArgs e)
        {
            bool? isChecked = propWhenTurningCheckBox.IsChecked;
            if (isChecked.HasValue && isChecked.Value)
            {
                _propWhenTurningChecked = true;

                if (adaptiveDirectionBoundsCheckBox.IsChecked.HasValue && adaptiveDirectionBoundsCheckBox.IsChecked.Value)
                {
                    adaptiveDirectionBoundsCheckBox.IsChecked = false;
                }
            }
            else
            {
                _propWhenTurningChecked = false;
            }
        }

        private void directionByLiftButton_CheckedOrUnchecked(object sender, RoutedEventArgs e)
        {
            bool? isChecked = directionByLiftButton.IsChecked;
            if (isChecked.HasValue && isChecked.Value)
            {
                _isDirectionByLiftPressed = true;
            }
            else
            {
                _isDirectionByLiftPressed = false;
            }
        }

        private void propWhenTurningSettings_TextChanged(object sender, TextChangedEventArgs e)
        {
            ReadPropWhenTurningSettings();
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

        private void ReadPropWhenTurningSettings()
        {
            byte propWhenTurningValue;

            if (byte.TryParse(propWhenTurningSettings.Text, out propWhenTurningValue))
            {
                _propWhenTurningValue = propWhenTurningValue;
            }
            else
            {
                _propWhenTurningValue = null;
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

            if (_isBoostPressed)
            {
                boostButton.IsChecked = false;
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

            if (_directionValue != _middleDirectionValue && _propWhenTurningChecked && 
                _propWhenTurningValue.HasValue && _propWhenTurningValue.Value < propulsion.Value ) 
            {
                propulsion.Value = _propWhenTurningValue.Value;
            }

            if (_isBoostPressed)
            {
                boostButton.IsChecked = false;
            }
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
                await ApplySetup(
                    _setupDialog.SocketAddress, 
                    _setupDialog.LiftSetupText, 
                    _setupDialog.PropSetupText, 
                    _setupDialog.ServoSetupText, 
                    _setupDialog.LiftPulsesRatios, 
                    _setupDialog.PropPulsesRatios, 
                    _setupDialog.ServoPulseCorrection,
                    _setupDialog.BoostSetup);

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
                boostButton.IsChecked = false;
                directionByLiftButton.IsChecked = false;

                propulsion.Value = 0;
                lift.Value = 0;
                direction.Value = 50;

                _isBoostPressed = false;
                _isDirectionByLiftPressed = false;

                _propulsionValue = 0;
                _liftValue = 0;
                _directionValue = 50;
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
                string exceptionText = null;
                _controlAckText.Clear();

                try
                {
                    _controlPacket[0] = _enabledValue;
                    _controlPacket[1] = _liftValue;
                    _controlPacket[2] = _propulsionValue;
                    _controlPacket[3] = _directionValue;

                    _controlPacket[4] = _isBoostPressed ? (byte)1 : (byte)0;
                    _controlPacket[5] = _isDirectionByLiftPressed ? (byte)1 : (byte)0;

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

                            Interlocked.Exchange(ref _lastSecAckCounter, 0);
                            _ackCounterStopwatch.Restart();

                            //je tente de lisser l'exec du garbage collector pour diluer/minimiser les lags
                            forceGC = true;
                        }
                    }

                    if (ReferenceEquals(exceptionMessage, null) && !ReferenceEquals(response, null) && ReferenceEquals(response.Exception, null))
                    {
                        if (response.AckResponseLength > 0)
                        {
                            _controlAckText.Append(response.AckResponseBuffer, 0, response.AckResponseLength);
                        }
                    }
                    else
                    {
                        if (!ReferenceEquals(response, null) && response.AckResponseLength > 0)
                        {
                            _controlAckText.Append(response.AckResponseBuffer, 0, response.AckResponseLength);
                        }

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

                            controlAckTextBlock.Text = _controlAckText.ToString();
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
            string liftPulsesRatiosText, 
            string propPulsesRatiosText, 
            string servoPulseCorrectionText,
            string boostSetup)
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

            if (!UpdateSetupPacket(liftSetupText, propSetupText, servoSetupText, liftPulsesRatiosText, propPulsesRatiosText, servoPulseCorrectionText, boostSetup))
                return;

            await _semaphoreSlim.WaitAsync();
            try
            {
                HovercraftRequestResult response = null;
                string exceptionMessage = null;

                response = _setupRecycledResult;
                _setupRecycledResult.Recycle();
                _setupAckText.Clear();

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
                    if (response.AckResponseLength > 0)
                    {
                        _setupAckText.Append(response.AckResponseBuffer, 0, response.AckResponseLength);
                    }

                    setupAckTextBlock.Text = _setupAckText.ToString();
                    exceptionTextBlock.Text = String.Empty;
                    return;
                }

                if (response.AckResponseLength > 0)
                {
                    _setupAckText.Append(response.AckResponseBuffer, 0, response.AckResponseLength);
                }

                setupAckTextBlock.Text = _setupAckText.ToString();

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

                //result.AckResponse = await reader.ReadLineAsync();
                result.AckResponseLength = await reader.ReadAsync(result.AckResponseBuffer, 0, result.AckResponseBuffer.Length);

                //j'enleve le CRLF a la main car je ne veux pas toucher au code du ESP32 pour ne pas perturber l'appli de Marc
                if (result.AckResponseLength > 2)
                {
                    if (result.AckResponseBuffer[result.AckResponseLength-2] == 13 && result.AckResponseBuffer[result.AckResponseLength - 1] == 10)
                    {
                        result.AckResponseBuffer[result.AckResponseLength-2] = (char)0x00;
                        result.AckResponseBuffer[result.AckResponseLength-1] = (char)0x00;
                        result.AckResponseLength = result.AckResponseLength-2;
                    }
                }
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
            string liftPulsesRatiosText, 
            string propPulsesRatiosText, 
            string servoPulseCorrectionText,
            string boostSetupText)
        {
            bool result = false;
            try
            {
                var liftSetup = ParseLedcSetupData(liftSetupText);
                var propSetup = ParseLedcSetupData(propSetupText);
                var servoSetup = ParseLedcSetupData(servoSetupText);

                var lcx = liftPulsesRatiosText.Trim().Split(';');
                if (lcx.Length != 2)
                    throw new ArgumentException(nameof(liftPulsesRatiosText));

                Byte lift1PulseCorrection = Byte.Parse(lcx[0].Replace("%", ""));
                Byte lift2PulseCorrection = Byte.Parse(lcx[1].Replace("%", ""));

                var pcx = propPulsesRatiosText.Trim().Split(';');
                if (pcx.Length != 2)
                    throw new ArgumentException(nameof(propPulsesRatiosText));

                Byte prop1PulseCorrection = Byte.Parse(pcx[0].Replace("%", ""));
                Byte prop2PulseCorrection = Byte.Parse(pcx[1].Replace("%", ""));

                Int16 servoCorrection = Int16.Parse(servoPulseCorrectionText);

                UInt16 boostSetup = UInt16.Parse(boostSetupText);

                int offset = 0;
                WriteByte(2, _setupPacket, ref offset);

                WriteBytes(liftSetup, _setupPacket, ref offset);
                WriteBytes(propSetup, _setupPacket, ref offset);
                WriteBytes(servoSetup, _setupPacket, ref offset);

                WriteByte(lift1PulseCorrection, _setupPacket, ref offset);
                WriteByte(lift2PulseCorrection, _setupPacket, ref offset);

                WriteByte(prop1PulseCorrection, _setupPacket, ref offset);
                WriteByte(prop2PulseCorrection, _setupPacket, ref offset);

                WriteBytes(servoCorrection, _setupPacket, ref offset);

                WriteBytes(boostSetup, _setupPacket, ref offset);

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
            public char[] AckResponseBuffer = new char[1024];

            public int AckResponseLength = 0;

            public string Exception = null;

            public void Recycle()
            {
                //Array.Clear(AckResponseBuffer, 0, AckResponseBuffer.Length);
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