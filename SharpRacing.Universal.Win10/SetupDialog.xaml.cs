using Windows.UI.Xaml.Controls;

namespace SharpRacing.Universal.Win10
{
    public sealed partial class SetupDialog : ContentDialog
    {
        public const string DefaultEscSetupText = "500;16;0;100;32767;65535";

        public const string DefaultServoSetupText = "500;16;0;100;32767;65535";

        public const string DefaultSocket = "192.168.4.1:8077";

        public const string DefaultControlIntervalMilliseconds = "0";

        public const string DefaultLiftPulseCorrections = "0;1024";

        public const string DefaultPropPulseCorrections = "0;512";

        public SetupDialog()
        {
            this.InitializeComponent();

            socketTextBox.Text = DefaultSocket;
            liftSetupTextBox.Text = DefaultEscSetupText;
            propSetupTextBox.Text = DefaultEscSetupText;

            servoSetupTextBox.Text = DefaultServoSetupText;
            controlIntervalMillisecondsTextBox.Text = DefaultControlIntervalMilliseconds;

            liftPulseCorrectionsTextBox.Text = DefaultLiftPulseCorrections;
            propPulseCorrectionsTextBox.Text = DefaultPropPulseCorrections;
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

        public string LiftPulseCorrections
        {
            get { return liftPulseCorrectionsTextBox.Text; }
            set { liftPulseCorrectionsTextBox.Text = value; }
        }

        public string PropPulseCorrections
        {
            get { return propPulseCorrectionsTextBox.Text; }
            set { propPulseCorrectionsTextBox.Text = value; }
        }

        public string ServoCorrection
        {
            get { return servoCorrectionTextBox.Text; }
            set { servoCorrectionTextBox.Text = value; }
        }

        string backupSocket;
        string backupLiftSetup;
        string backupPropSetup;
        string backupServoSetup;
        string backupControlIntervalMilliseconds;
        string backupLiftPulseCorrections;
        string backupPropPulseCorrections;
        string backupServoCorrection;

        private void ContentDialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
        {
            backupSocket = socketTextBox.Text;
            backupLiftSetup = liftSetupTextBox.Text;
            backupPropSetup = propSetupTextBox.Text;
            backupServoSetup = servoSetupTextBox.Text;
            backupControlIntervalMilliseconds = controlIntervalMillisecondsTextBox.Text;
            backupLiftPulseCorrections = liftPulseCorrectionsTextBox.Text;
            backupPropPulseCorrections = propPulseCorrectionsTextBox.Text;
            backupServoCorrection = servoCorrectionTextBox.Text;
        }

        //PrimaryButtonText="Cancel"
        private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            socketTextBox.Text = backupSocket;
            liftSetupTextBox.Text = backupLiftSetup;
            propSetupTextBox.Text = backupPropSetup;
            servoSetupTextBox.Text = backupServoSetup;
            controlIntervalMillisecondsTextBox.Text = backupControlIntervalMilliseconds;

            liftPulseCorrectionsTextBox.Text = backupLiftPulseCorrections;
            propPulseCorrectionsTextBox.Text = backupPropPulseCorrections;
            servoCorrectionTextBox.Text = backupServoCorrection;
        }

        //SecondaryButtonText="CONFIRM"
        private void ContentDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
        }
    }
}