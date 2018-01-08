using Windows.UI.Xaml.Controls;

namespace SharpRacing.Universal.Win10
{
    public sealed partial class SetupDialog : ContentDialog
    {
        // 500Hz | 16bit | 0 ... 100 | 32767 ... 65535 -> pulse is 1ms ... 2ms 
        private const string DefaultEscSetupText = "500;16;0;100;32767;65535";

        // 500Hz | 16bit | 0 ... 100 | 32767 ... 65535 -> pulse is 1ms ... 2ms 
        private const string DefaultServoSetupText = "500;16;0;100;32767;65535";

        private const string DefaultSocket = "192.168.4.1:8077";

        private const string DefaultControlIntervalMilliseconds = "0";

        private const string DefaultLiftPulsesRatios = "100;100";

        private const string DefaultPropPulsesRatios = "100;100";

        private const string DefaultServoPulseCorrection = "0";

        private string _socketBackup;

        private string _liftSetupBackup;

        private string _propSetupBackup;

        private string _servoSetupBackup;

        private string _controlIntervalMillisecondsBackup;

        private string _liftPulseRatiosBackup;

        private string _propPulseRatiosBackup;

        private string _servoPulseCorrectionBackup;

        public SetupDialog()
        {
            this.InitializeComponent();

            socketTextBox.Text = DefaultSocket;
            liftSetupTextBox.Text = DefaultEscSetupText;
            propSetupTextBox.Text = DefaultEscSetupText;

            servoSetupTextBox.Text = DefaultServoSetupText;
            controlIntervalMillisecondsTextBox.Text = DefaultControlIntervalMilliseconds;

            liftPulsesRatiosTextBox.Text = DefaultLiftPulsesRatios;
            propPulsesRatiosTextBox.Text = DefaultPropPulsesRatios;
            servoPulseCorrectionTextBox.Text = DefaultServoPulseCorrection;
        }

        public string SocketAddress
        {
            get { return socketTextBox.Text; }
            set { socketTextBox.Text = value; }
        }

        public string LiftSetupText
        {
            get { return liftSetupTextBox.Text; }
            set { liftSetupTextBox.Text = value; }
        }

        public string PropSetupText
        {
            get { return propSetupTextBox.Text; }
            set { propSetupTextBox.Text = value; }
        }

        public string ServoSetupText
        {
            get { return servoSetupTextBox.Text; }
            set { servoSetupTextBox.Text = value; }
        }

        public string ControlIntervalMilliseconds
        {
            get { return controlIntervalMillisecondsTextBox.Text; }
            set { controlIntervalMillisecondsTextBox.Text = value; }
        }

        public string LiftPulsesRatios
        {
            get { return liftPulsesRatiosTextBox.Text; }
            set { liftPulsesRatiosTextBox.Text = value; }
        }

        public string PropPulsesRatios
        {
            get { return propPulsesRatiosTextBox.Text; }
            set { propPulsesRatiosTextBox.Text = value; }
        }

        public string ServoPulseCorrection
        {
            get { return servoPulseCorrectionTextBox.Text; }
            set { servoPulseCorrectionTextBox.Text = value; }
        }

        private void ContentDialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
        {
            _socketBackup = socketTextBox.Text;
            _liftSetupBackup = liftSetupTextBox.Text;
            _propSetupBackup = propSetupTextBox.Text;
            _servoSetupBackup = servoSetupTextBox.Text;
            _controlIntervalMillisecondsBackup = controlIntervalMillisecondsTextBox.Text;
            _liftPulseRatiosBackup = liftPulsesRatiosTextBox.Text;
            _propPulseRatiosBackup = propPulsesRatiosTextBox.Text;
            _servoPulseCorrectionBackup = servoPulseCorrectionTextBox.Text;
        }

        //PrimaryButtonText="Cancel"
        private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            socketTextBox.Text = _socketBackup;
            liftSetupTextBox.Text = _liftSetupBackup;
            propSetupTextBox.Text = _propSetupBackup;
            servoSetupTextBox.Text = _servoSetupBackup;
            controlIntervalMillisecondsTextBox.Text = _controlIntervalMillisecondsBackup;

            liftPulsesRatiosTextBox.Text = _liftPulseRatiosBackup;
            propPulsesRatiosTextBox.Text = _propPulseRatiosBackup;
            servoPulseCorrectionTextBox.Text = _servoPulseCorrectionBackup;
        }

        //SecondaryButtonText="CONFIRM"
        private void ContentDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
        }
    }
}