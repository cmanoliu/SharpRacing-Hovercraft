#include <WiFi.h>
#include "Secrets.h"

const char *ssid = WIFI_SSID;
const char *password = WIFI_PASSWORD; //defined in Secrets.h

//#define DEBUG

#define TCP_PORT 8077
#define TICK_TIMEOUT 10
#define BROKEN_CONNECTION_TIMEOUT 1300 

#define LIFT1_CHANNEL 0
#define LIFT2_CHANNEL 1
#define PROP1_CHANNEL 2
#define PROP2_CHANNEL 3
#define SERVO_CHANNEL 4

// The first thing to think about a PWM signal to be generated is its frequency. 
// We can choose a channel from 0 to 15 and a resolution between 1 and 16 bits. 
// See: https://www.hackster.io/mjrobot/iot-made-simple-playing-with-the-esp32-on-arduino-ide-0fe58c
// Also see: https://github.com/jkb-git/ESP32Servo/blob/master/examples/Multiple-Servo-Example-ESP32/Multiple-Servo-Example-ESP32.ino

//Turnigy D561MG Coreless DS/MG Servo 24T 1.3kg/0.08sec/7.5g
//https://hobbyking.com/en_us/d561mg-digital-servo.html
#define DEFAULT_SERVO_LEDC_FREQ            500 // 500 Hz
#define DEFAULT_SERVO_LEDC_RESOLUTION_BITS  16 // 16 bits
#define DEFAULT_SERVO_MAP_IN_MIN 0
#define DEFAULT_SERVO_MAP_IN_MAX 100
#define DEFAULT_SERVO_MAP_OUT_MIN 32767 // 1ms at 500 Hz / 16 bits
#define DEFAULT_SERVO_MAP_OUT_MAX 65535 // 2ms at 500 Hz / 16 bits

//4x Quanum BE1806-2700kv Race Edition Brushless Motor 3~4S (CW & CCW)
//https://hobbyking.com/en_us/be1806p-2700kv-purple-color-with-purple-nylon-nut-cw.html
//4x Turnigy MultiStar 32Bit 20A Race Spec ESC 2~4s (OPTO)
//https://hobbyking.com/en_us/multistar-32bit-20a-0-lite-before-is-18a.html
#define DEFAULT_ESC_LEDC_FREQ            500 // 500 Hz
#define DEFAULT_ESC_LEDC_RESOLUTION_BITS  16 //  16 bits
#define DEFAULT_ESC_MAP_IN_MIN 0
#define DEFAULT_ESC_MAP_IN_MAX 100
#define DEFAULT_ESC_MAP_OUT_MIN 32767 // 1ms at 500 Hz / 16 bits 
#define DEFAULT_ESC_MAP_OUT_MAX 65535 // 2ms at 500 Hz / 16 bits 

#define DEFAULT_lift1_pulse_ratio 1.0f // 100%
#define DEFAULT_lift2_pulse_ratio 1.0f // 100%
#define DEFAULT_prop1_pulse_ratio 1.0f // 100%
#define DEFAULT_prop2_pulse_ratio 1.0f // 100%
#define DEFAULT_servo_pulse_correction 0

#define DEFAULT_prop_boost_pulse 51199 //  56%  

#define CONTROL_PACKET_LENGTH_V1 4
#define CONTROL_PACKET_LENGTH_V2 6
#define SETUP_PACKET_LENGTH   72

#define INPUT_BUFFER_LENGTH   512
#define OUTPUT_BUFFER_LENGTH  768

WiFiServer server(TCP_PORT);

uint8_t inputBuffer[INPUT_BUFFER_LENGTH];
char    outputBuffer[OUTPUT_BUFFER_LENGTH];

uint32_t servo_ledc_freq = DEFAULT_SERVO_LEDC_FREQ;
uint8_t  servo_ledc_resolution_bits = DEFAULT_SERVO_LEDC_RESOLUTION_BITS;
uint32_t servo_map_in_min = DEFAULT_SERVO_MAP_IN_MIN;
uint32_t servo_map_in_max = DEFAULT_SERVO_MAP_IN_MAX;
uint32_t servo_map_out_min = DEFAULT_SERVO_MAP_OUT_MIN;
uint32_t servo_map_out_max = DEFAULT_SERVO_MAP_OUT_MAX;

int16_t servo_pulse_correction = DEFAULT_servo_pulse_correction;

uint32_t lift_ledc_freq = DEFAULT_ESC_LEDC_FREQ;
uint8_t  lift_ledc_resolution_bits = DEFAULT_ESC_LEDC_RESOLUTION_BITS;
uint32_t lift_map_in_min = DEFAULT_ESC_MAP_IN_MIN;
uint32_t lift_map_in_max = DEFAULT_ESC_MAP_IN_MAX;
uint32_t lift_map_out_min = DEFAULT_ESC_MAP_OUT_MIN;
uint32_t lift_map_out_max = DEFAULT_ESC_MAP_OUT_MAX;

float_t lift1_pulse_correction = DEFAULT_lift1_pulse_ratio;
float_t lift2_pulse_correction = DEFAULT_lift2_pulse_ratio;

uint32_t prop_ledc_freq = DEFAULT_ESC_LEDC_FREQ;
uint8_t  prop_ledc_resolution_bits = DEFAULT_ESC_LEDC_RESOLUTION_BITS;
uint32_t prop_map_in_min = DEFAULT_ESC_MAP_IN_MIN;
uint32_t prop_map_in_max = DEFAULT_ESC_MAP_IN_MAX;
uint32_t prop_map_out_min = DEFAULT_ESC_MAP_OUT_MIN;
uint32_t prop_map_out_max = DEFAULT_ESC_MAP_OUT_MAX;

uint16_t prop_boost_pulse = DEFAULT_prop_boost_pulse;

float_t prop1_pulse_correction = DEFAULT_prop1_pulse_ratio;
float_t prop2_pulse_correction = DEFAULT_prop2_pulse_ratio;

unsigned long cnt = 0;

// the setup function runs once when you press reset or power the board
void setup() {
	Serial.begin(115200);
	WiFi.onEvent(WiFiEvent);

	/* Setup WiFi AccessPoint */
	setupWiFiAccessPoint();

	/* Start (TCP) Server */
	startTcpServer();

	/* Setup Hovercraft */
	setupHovercraft();
}

// the loop function runs over and over again until power down or reset
void loop() {
	static unsigned long last = 0;
	static unsigned int delta = TICK_TIMEOUT;

	unsigned long now = millis();
	unsigned long diff = now - last;

	unsigned int tick = (diff >= delta);

	if (tick) {
		delta = TICK_TIMEOUT;
		last = now;
		serverPoll();

		stopHovercraft();
	}
}

void setupWiFiAccessPoint() {
	Serial.println("Configuring WiFi Access Point...");
	WiFi.disconnect();
	/* remove the password parameter if you want the AP to be open. */
	WiFi.softAP(ssid, password);
	IPAddress myIP = WiFi.softAPIP();
	Serial.print("IP address: ");
	Serial.println(myIP);
}

void startTcpServer() {
	Serial.print("Starting TCP server on port ");
	Serial.print(TCP_PORT);
	Serial.println(" ...");

	server.begin();

	/* With nodelay set to true, Nagle will be disabled.
	The Nagle algorithm is intended to reduce TCP/IP traffic of small packets sent over the network
	by combining a number of small outgoing messages, and sending them all at once.
	The downside of such approach is delaying individual messages until a big enough packet is assembled. */
	// https://github.com/esp8266/Arduino/blob/master/doc/esp8266wifi/server-class.rst
	server.setNoDelay(true);

	Serial.println("TCP server is now running.");
}

void ledcSetup_SERVO(uint32_t freq, uint8_t resolution_bits, uint32_t map_in_min, uint32_t map_in_max, uint32_t map_out_min, uint32_t map_out_max, int16_t pulse_correction) {
	servo_ledc_freq = freq;
	servo_ledc_resolution_bits = resolution_bits;
	servo_map_in_min = map_in_min;
	servo_map_in_max = map_in_max;
	servo_map_out_min = map_out_min;
	servo_map_out_max = map_out_max;

	servo_pulse_correction = pulse_correction;

	ledcSetup(SERVO_CHANNEL, servo_ledc_freq, servo_ledc_resolution_bits);

#if defined(DEBUG)
	Serial.print("ledcSetup( SERVO_CHANNEL #"); Serial.print(SERVO_CHANNEL); Serial.print(", ");
	Serial.print(servo_ledc_freq); Serial.print(", "); Serial.print(servo_ledc_resolution_bits);
	Serial.print("); pulse correction: "); Serial.print(servo_pulse_correction);
	Serial.println("); ");
#endif
}

void ledcSetup_LIFT(uint32_t freq, uint8_t resolution_bits, uint32_t map_in_min, uint32_t map_in_max, uint32_t map_out_min, uint32_t map_out_max, float_t pulse1_correction, float_t pulse2_correction) {
	lift_ledc_freq = freq;
	lift_ledc_resolution_bits = resolution_bits;
	lift_map_in_min = map_in_min;
	lift_map_in_max = map_in_max;
	lift_map_out_min = map_out_min;
	lift_map_out_max = map_out_max;

	lift1_pulse_correction = pulse1_correction;
	lift2_pulse_correction = pulse2_correction;

	ledcSetup(LIFT1_CHANNEL, lift_ledc_freq, lift_ledc_resolution_bits);
	ledcSetup(LIFT2_CHANNEL, lift_ledc_freq, lift_ledc_resolution_bits);

#if defined(DEBUG)
	Serial.print("ledcSetup( LIFT1_CHANNEL #"); Serial.print(LIFT1_CHANNEL);  Serial.print(" and LIFT2_CHANNEL #"); Serial.print(LIFT2_CHANNEL); Serial.print(", ");
	Serial.print(lift_ledc_freq); Serial.print(", "); Serial.print(lift_ledc_resolution_bits);
	Serial.print("); pulse ratios: "); Serial.print(lift1_pulse_correction); Serial.print(", "); Serial.print(lift2_pulse_correction);
	Serial.println(";");
#endif
}

void ledcSetup_PROP(uint32_t freq, uint8_t resolution_bits, uint32_t map_in_min, uint32_t map_in_max, uint32_t map_out_min, uint32_t map_out_max, float_t pulse1_correction, float_t pulse2_correction, uint16_t boost_pulse) {
	prop_ledc_freq = freq;
	prop_ledc_resolution_bits = resolution_bits;
	prop_map_in_min = map_in_min;
	prop_map_in_max = map_in_max;
	prop_map_out_min = map_out_min;
	prop_map_out_max = map_out_max;

	prop_boost_pulse = boost_pulse;

	prop1_pulse_correction = pulse1_correction;
	prop2_pulse_correction = pulse2_correction;

	ledcSetup(PROP1_CHANNEL, prop_ledc_freq, prop_ledc_resolution_bits);
	ledcSetup(PROP2_CHANNEL, prop_ledc_freq, prop_ledc_resolution_bits);

#if defined(DEBUG)
	Serial.print("ledcSetup( PROP1_CHANNEL #"); Serial.print(PROP1_CHANNEL);  Serial.print(" and PROP2_CHANNEL #"); Serial.print(PROP2_CHANNEL); Serial.print(", ");
	Serial.print(prop_ledc_freq); Serial.print(", "); Serial.print(prop_ledc_resolution_bits);
	Serial.print("); pulse ratios: "); Serial.print(prop1_pulse_correction); Serial.print(", "); Serial.print(prop2_pulse_correction);
	Serial.print("; boost_pulse: "); Serial.print(boost_pulse);
	Serial.println(";");
#endif
}

void setupHovercraft() {
	ledcSetup_SERVO(
		DEFAULT_SERVO_LEDC_FREQ,
		DEFAULT_SERVO_LEDC_RESOLUTION_BITS,
		DEFAULT_SERVO_MAP_IN_MIN,
		DEFAULT_SERVO_MAP_IN_MAX,
		DEFAULT_SERVO_MAP_OUT_MIN,
		DEFAULT_SERVO_MAP_OUT_MAX,
		DEFAULT_servo_pulse_correction);

	ledcSetup_LIFT(
		DEFAULT_ESC_LEDC_FREQ,
		DEFAULT_ESC_LEDC_RESOLUTION_BITS,
		DEFAULT_ESC_MAP_IN_MIN,
		DEFAULT_ESC_MAP_IN_MAX,
		DEFAULT_ESC_MAP_OUT_MIN,
		DEFAULT_ESC_MAP_OUT_MAX,
		DEFAULT_lift1_pulse_ratio,
		DEFAULT_lift2_pulse_ratio);

	ledcSetup_PROP(
		DEFAULT_ESC_LEDC_FREQ,
		DEFAULT_ESC_LEDC_RESOLUTION_BITS,
		DEFAULT_ESC_MAP_IN_MIN,
		DEFAULT_ESC_MAP_IN_MAX,
		DEFAULT_ESC_MAP_OUT_MIN,
		DEFAULT_ESC_MAP_OUT_MAX,
		DEFAULT_prop1_pulse_ratio,
		DEFAULT_prop2_pulse_ratio,
		DEFAULT_prop_boost_pulse);

	// 13 - This is GPIO #13 and also an analog input A12.
	// It's also connected to the red LED next to the USB port
	ledcAttachPin(13, SERVO_CHANNEL);

	// A1 - this is an analog input A1 and also an analog output DAC1.
	// It can also be used as a GPIO #25
	ledcAttachPin(25, LIFT1_CHANNEL);

	// A0 - this is an analog input A0 and also an analog output DAC2. 
	// It can also be used as a GPIO #26
	ledcAttachPin(26, LIFT2_CHANNEL);

	// 32 - This is GPIO #32 and also an analog input A7.
	// It can also be used to connect a 32 KHz crystal.
	ledcAttachPin(32, PROP1_CHANNEL);

	// 33 - This is GPIO #33 and also an analog input A9.
	// It can also be used to connect a 32 KHz crystal.
	ledcAttachPin(33, PROP2_CHANNEL);

	stopHovercraft();
}

void write_SERVO(byte duty) {
	uint32_t pulse = map(
		duty,
		servo_map_in_min,
		servo_map_in_max,
		servo_map_out_min,
		servo_map_out_max);

#if defined(DEBUG)
	Serial.print("map(");
	Serial.print(duty); Serial.print(", ");
	Serial.print(servo_map_in_min); Serial.print(", ");
	Serial.print(servo_map_in_max); Serial.print(", ");
	Serial.print(servo_map_out_min); Serial.print(", ");
	Serial.print(servo_map_out_max); Serial.print("); ");
#endif

	uint32_t finalPulse = pulse + servo_pulse_correction;

	ledcWrite(SERVO_CHANNEL, constrain(finalPulse, servo_map_out_min, servo_map_out_max));

#if defined(DEBUG)
	Serial.print("ledc wrote "); Serial.print(finalPulse); Serial.print(" to SERVO channel : ");
	Serial.print(SERVO_CHANNEL); Serial.println("; ");
#endif  
}

byte servoDuty_middle = 50;

void write_LIFT(byte liftDuty, byte turnByLiftEnabled, byte servoDuty) {

	uint32_t pulse = map(
		liftDuty,
		lift_map_in_min,
		lift_map_in_max,
		lift_map_out_min,
		lift_map_out_max);

#if defined(DEBUG)
	Serial.print("map(");
	Serial.print(liftDuty); Serial.print(", ");
	Serial.print(lift_map_in_min); Serial.print(", ");
	Serial.print(lift_map_in_max); Serial.print(", ");
	Serial.print(lift_map_out_min); Serial.print(", ");
	Serial.print(lift_map_out_max); Serial.print("); ");
#endif  

	uint32_t pulse1 = pulse * lift1_pulse_correction;
	uint32_t pulse2 = pulse * lift2_pulse_correction;

	if (turnByLiftEnabled)
	{

#if defined(DEBUG)  
		Serial.print("turnByLiftEnabled; ");
#endif    

		//LIFT1_CHANNEL turns LEFT
		//LIFT2_CHANNEL turns RIGHT

		//Full LEFT when servoDuty is at 100 -> LEFT+50% and RIGHT-50% 
		if (servoDuty > servoDuty_middle)
		{
			uint32_t delta = servoDuty - servoDuty_middle;
			pulse1 = pulse1 + (pulse1 - lift_map_out_min) * delta / 100;
			pulse2 = pulse2 - (pulse2 - lift_map_out_min) * delta / 100;

			if (pulse1 > lift_map_out_max)
			{
				uint32_t exceeding = pulse1 - lift_map_out_max;
				pulse1 = pulse1 - exceeding;
				pulse2 = pulse2 - exceeding;
			}
		}
		//Full RIGHT when servoDuty is at 0 -> LEFT-50% and RIGHT+50%
		else if (servoDuty < servoDuty_middle)
		{
			uint32_t delta = servoDuty_middle - servoDuty;
			pulse1 = pulse1 - (pulse1 - lift_map_out_min) * delta / 100;
			pulse2 = pulse2 + (pulse2 - lift_map_out_min) * delta / 100;

			if (pulse2 > lift_map_out_max)
			{
				uint32_t exceeding = pulse2 - lift_map_out_max;
				pulse2 = pulse2 - exceeding;
				pulse1 = pulse1 - exceeding;
			}
		}
	}

	ledcWrite(LIFT1_CHANNEL, constrain(pulse1, lift_map_out_min, lift_map_out_max));
	ledcWrite(LIFT2_CHANNEL, constrain(pulse2, lift_map_out_min, lift_map_out_max));

#if defined(DEBUG)  
	Serial.print("ledc wrote "); Serial.print(pulse1); Serial.print(", "); Serial.print(pulse2); Serial.print(" to LIFT channels : ");
	Serial.print(LIFT1_CHANNEL); Serial.print(", "); Serial.print(LIFT2_CHANNEL); Serial.println("; ");
#endif    
}

void write_PROP(byte duty, byte boostEnabled) {

	uint32_t pulse1;
	uint32_t pulse2;

	if (boostEnabled)
	{
		pulse1 = prop_boost_pulse;
		pulse2 = prop_boost_pulse;
	}
	else
	{
		uint32_t pulse = map(
			duty,
			prop_map_in_min,
			prop_map_in_max,
			prop_map_out_min,
			prop_map_out_max);

#if defined(DEBUG)  
		Serial.print("map(");
		Serial.print(duty); Serial.print(", ");
		Serial.print(prop_map_in_min); Serial.print(", ");
		Serial.print(prop_map_in_max); Serial.print(", ");
		Serial.print(prop_map_out_min); Serial.print(", ");
		Serial.print(prop_map_out_max); Serial.print("); ");
#endif 

		pulse1 = pulse * prop1_pulse_correction;
		pulse2 = pulse * prop2_pulse_correction;
	}

	ledcWrite(PROP1_CHANNEL, constrain(pulse1, prop_map_out_min, prop_map_out_max));
	ledcWrite(PROP2_CHANNEL, constrain(pulse2, prop_map_out_min, prop_map_out_max));

#if defined(DEBUG)  
	Serial.print("ledc wrote "); Serial.print(pulse1); Serial.print(", "); Serial.print(pulse2); Serial.print(" to PROP channels : ");
	Serial.print(PROP1_CHANNEL); Serial.print(", "); Serial.print(PROP2_CHANNEL); Serial.println("; ");
#endif   
}

void updateHovercraft(byte enabled, byte liftDuty, byte propDuty, byte servoDuty, byte boostEnabled, byte turnByLiftEnabled)
{
	if (!enabled)
	{
		stopHovercraft();
		return;
	}

	if (turnByLiftEnabled)
	{
		ledcWrite(SERVO_CHANNEL, servo_map_out_min + (servo_map_out_max - servo_map_out_min) / 2); //TODO CONSIDER >> 1
	}
	else
	{
		write_SERVO(servoDuty);
	}

	write_LIFT(liftDuty, turnByLiftEnabled, servoDuty);
	write_PROP(propDuty, boostEnabled);
}

void stopHovercraft()
{
	ledcWrite(PROP1_CHANNEL, prop_map_out_min);
	ledcWrite(PROP2_CHANNEL, prop_map_out_min);

	ledcWrite(LIFT1_CHANNEL, lift_map_out_min);
	ledcWrite(LIFT2_CHANNEL, lift_map_out_min);

	ledcWrite(SERVO_CHANNEL, servo_map_out_min + (servo_map_out_max - servo_map_out_min) / 2); //TODO CONSIDER >> 1
}

uint32_t readInt32(uint8_t* data, uint32_t offset)
{
	// big-endian
	byte byte0 = data[offset];
	byte byte1 = data[offset + 1];
	byte byte2 = data[offset + 2];
	byte byte3 = data[offset + 3];

	uint32_t uint32 = ((uint32_t)byte0 << 24) | ((uint32_t)byte1 << 16) | ((uint32_t)byte2 << 8) | (uint32_t)byte3;
	return uint32;
}

uint16_t readUInt16(uint8_t* data, uint32_t offset)
{
	// big-endian
	byte byte0 = data[offset];
	byte byte1 = data[offset + 1];

	uint16_t uint16 = ((uint16_t)byte0 << 8) | (uint16_t)byte1;
	return uint16;
}

int16_t readInt16(uint8_t* data, uint32_t offset)
{
	// big-endian
	byte byte0 = data[offset];
	byte byte1 = data[offset + 1];

	int16_t int16 = ((int16_t)byte0 << 8) | (int16_t)byte1;
	return int16;
}

void serverPoll() {

	unsigned int last_packet_timestamp = millis();

	/* listen for client */
	WiFiClient client = server.available();

	if (client) {
		Serial.print("New TCP client, IP address: ");
		Serial.print(client.remoteIP());
		Serial.print(":");
		Serial.println(client.remotePort());

		while (client.connected()) {

			if (client.available()) {

				last_packet_timestamp = millis();

				// Read
				int tmp_len = client.read(inputBuffer, INPUT_BUFFER_LENGTH);

#if defined(DEBUG)           
				Serial.print("Received ");
				Serial.print(tmp_len);
				Serial.println(" bytes.");
#endif 

				switch (tmp_len) {

				case CONTROL_PACKET_LENGTH_V1:
				case CONTROL_PACKET_LENGTH_V2:
				{
					byte tmp_enabled = inputBuffer[0];
					if (tmp_enabled == 0 || tmp_enabled == 1)
					{
						byte tmp_lift = inputBuffer[1];
						byte tmp_propulsion = inputBuffer[2];
						byte tmp_servo = inputBuffer[3];

						//L'applie iOS de Marc n'envoie pas les 2 nouveaux bytes du mode boost et turnByLift
						byte tmp_boost_enabled = 0;
						byte tmp_turnByLiftEnabled = 0;

						if (tmp_len == CONTROL_PACKET_LENGTH_V2)
						{
							tmp_boost_enabled = inputBuffer[4];
							tmp_turnByLiftEnabled = inputBuffer[5];
						}

						updateHovercraft(tmp_enabled, tmp_lift, tmp_propulsion, tmp_servo, tmp_boost_enabled, tmp_turnByLiftEnabled);
						sprintf(outputBuffer, "ACK#%u %u %u %u %u %u %u\r\n", cnt++, tmp_enabled, tmp_lift, tmp_propulsion, tmp_servo, tmp_boost_enabled, tmp_turnByLiftEnabled);

#if defined(DEBUG)          
						Serial.println((char *)outputBuffer);
#endif 
					}
					else
					{
						stopHovercraft();
						sprintf(outputBuffer, "Invalid %u bytes (control) packet. The first byte is expected to be 00 (disabled) or 01 (enabled). STOP.\r\n", CONTROL_PACKET_LENGTH_V2);
						Serial.println((char *)outputBuffer);
					}
					break;
				}

				// setup packet 
				// 64 bytes =  1 + 3 x 21 bytes (4, 1, 4, 4, 4, 4 = uint32_t freq, uint8_t resolution_bits, uint32_t map_in_min, uint32_t map_in_max, uint32_t map_out_min, uint32_t map_out_max )
				// +4 bytes = lift1_pulse_ratio, lift2_pulse_ratio, prop1_pulse_ratio, prop2_pulse_ratio
				// +2 bytes (1 word = servo_pulse_correction) 
				// +2 bytes (1 word = prop_boost_pulse) 
				case SETUP_PACKET_LENGTH:
				{
					byte first = inputBuffer[0];
					if (first == 2)
					{
						uint32_t i = 1;
						uint32_t tmp_lift_freq = readInt32(inputBuffer, 0 + i);         //  0,  1,  2,  3
						byte     tmp_lift_resolution = inputBuffer[4 + i];              //  4
						uint32_t tmp_lift_map_in_min = readInt32(inputBuffer, 5 + i);   //  5,  6,  7,  8
						uint32_t tmp_lift_map_in_max = readInt32(inputBuffer, 9 + i);   //  9, 10, 11, 12
						uint32_t tmp_lift_map_out_min = readInt32(inputBuffer, 13 + i); // 13, 14, 15, 16 
						uint32_t tmp_lift_map_out_max = readInt32(inputBuffer, 17 + i); // 17, 18, 19, 20 

						i = i + 21;
						uint32_t tmp_propulsion_freq = readInt32(inputBuffer, 0 + i);         //  0,  1,  2,  3 
						byte     tmp_propulsion_resolution = inputBuffer[4 + i];              //  4
						uint32_t tmp_propulsion_map_in_min = readInt32(inputBuffer, 5 + i);   //  5,  6,  7,  8 
						uint32_t tmp_propulsion_map_in_max = readInt32(inputBuffer, 9 + i);   //  9, 10, 11, 12 
						uint32_t tmp_propulsion_map_out_min = readInt32(inputBuffer, 13 + i); // 13, 14, 15, 16 
						uint32_t tmp_propulsion_map_out_max = readInt32(inputBuffer, 17 + i); // 17, 18, 19, 20 

						i = i + 21;
						uint32_t tmp_servo_freq = readInt32(inputBuffer, 0 + i);         //  0,  1,  2,  3 
						byte     tmp_servo_resolution = inputBuffer[4 + i];              //  4
						uint32_t tmp_servo_map_in_min = readInt32(inputBuffer, 5 + i);   //  5,  6,  7,  8 
						uint32_t tmp_servo_map_in_max = readInt32(inputBuffer, 9 + i);   //  9, 10, 11, 12 
						uint32_t tmp_servo_map_out_min = readInt32(inputBuffer, 13 + i); // 13, 14, 15, 16 
						uint32_t tmp_servo_map_out_max = readInt32(inputBuffer, 17 + i); // 17, 18, 19, 20 

						i = i + 21;
						float_t tmp_lift1_pulse_correction = inputBuffer[0 + i] / 100.0f;   // 0 
						float_t tmp_lift2_pulse_correction = inputBuffer[1 + i] / 100.0f;   // 1 
						float_t tmp_prop1_pulse_correction = inputBuffer[2 + i] / 100.0f;   // 2
						float_t tmp_prop2_pulse_correction = inputBuffer[3 + i] / 100.0f;   // 3
						int16_t tmp_servo_pulse_correction = readInt16(inputBuffer, 4 + i); //  4,  5

						i = i + 6;
						uint16_t tmp_prop_boost_pulse = readUInt16(inputBuffer, 0 + i); //  0,  1

						ledcSetup_LIFT(
							tmp_lift_freq,
							tmp_lift_resolution,
							tmp_lift_map_in_min,
							tmp_lift_map_in_max,
							tmp_lift_map_out_min,
							tmp_lift_map_out_max,
							tmp_lift1_pulse_correction,
							tmp_lift2_pulse_correction);

						ledcSetup_PROP(
							tmp_propulsion_freq,
							tmp_propulsion_resolution,
							tmp_propulsion_map_in_min,
							tmp_propulsion_map_in_max,
							tmp_propulsion_map_out_min,
							tmp_propulsion_map_out_max,
							tmp_prop1_pulse_correction,
							tmp_prop2_pulse_correction,
							tmp_prop_boost_pulse);

						ledcSetup_SERVO(
							tmp_servo_freq,
							tmp_servo_resolution,
							tmp_servo_map_in_min,
							tmp_servo_map_in_max,
							tmp_servo_map_out_min,
							tmp_servo_map_out_max,
							tmp_servo_pulse_correction);

						sprintf(outputBuffer, "ACK#%u LIFT %u %u %u %u %u %u %.2f:%.2f | PROP %u %u %u %u %u %u %.2f:%.2f %u | SERVO %u %u %u %u %u %u trim%d\r\n", cnt++,
							tmp_lift_freq, tmp_lift_resolution, tmp_lift_map_in_min, tmp_lift_map_in_max, tmp_lift_map_out_min, tmp_lift_map_out_max, tmp_lift1_pulse_correction, tmp_lift2_pulse_correction,
							tmp_propulsion_freq, tmp_propulsion_resolution, tmp_propulsion_map_in_min, tmp_propulsion_map_in_max, tmp_propulsion_map_out_min, tmp_propulsion_map_out_max, tmp_prop1_pulse_correction, tmp_prop2_pulse_correction, tmp_prop_boost_pulse,
							tmp_servo_freq, tmp_servo_resolution, tmp_servo_map_in_min, tmp_servo_map_in_max, tmp_servo_map_out_min, tmp_servo_map_out_max, tmp_servo_pulse_correction
						);

#if defined(DEBUG)          
						Serial.println((char *)outputBuffer);
#endif 
					}
					else
					{
						stopHovercraft();
						sprintf(outputBuffer, "Invalid %u bytes (setup) packet. The first byte is expected to be 02. STOP.\r\n", SETUP_PACKET_LENGTH);
						Serial.println((char *)outputBuffer);
					}
					break;
				}

				//unexpected length packet
				default:
				{
					stopHovercraft();
					sprintf(outputBuffer, "Invalid packet. Expected a %u bytes (control) packet or an %u bytes (setup) packet. Received packet is %u bytes length. STOP.\r\n", CONTROL_PACKET_LENGTH_V2, SETUP_PACKET_LENGTH, tmp_len);
					Serial.println((char *)outputBuffer);
					break;
				}
				}

				// Write
				tmp_len = strlen(outputBuffer);
				int rc = client.write(outputBuffer, tmp_len);
			}

			boolean isBrokenConnection = ((millis() - last_packet_timestamp) >= BROKEN_CONNECTION_TIMEOUT);
			if (isBrokenConnection) {
				Serial.println("BROKEN_CONNECTION_TIMEOUT. client.stop()");
				client.stop();
			}
		}
	}
}

void WiFiEvent(WiFiEvent_t event) {
	Serial.printf("[WiFi-event] event: %d | ", event);
	switch (event)
	{
	case SYSTEM_EVENT_WIFI_READY:
		Serial.println("SYSTEM_EVENT_WIFI_READY | ESP32 WiFi ready");
		break;
	case SYSTEM_EVENT_SCAN_DONE:
		Serial.println("SYSTEM_EVENT_SCAN_DONE | ESP32 finish scanning AP");
		break;
	case SYSTEM_EVENT_STA_START:
		Serial.println("SYSTEM_EVENT_STA_START | ESP32 station start");
		break;
	case SYSTEM_EVENT_STA_STOP:
		Serial.println("SYSTEM_EVENT_STA_STOP | ESP32 station stop");
		break;
	case SYSTEM_EVENT_STA_CONNECTED:
		Serial.println("SYSTEM_EVENT_STA_CONNECTED | ESP32 station connected to AP");
		break;
	case SYSTEM_EVENT_STA_GOT_IP:
		Serial.println("SYSTEM_EVENT_STA_GOT_IP ESP32 station got IP from connected AP");
		Serial.println("IP address: ");
		Serial.println(WiFi.localIP());
		break;
	case SYSTEM_EVENT_STA_DISCONNECTED:
		Serial.println("SYSTEM_EVENT_STA_DISCONNECTED | ESP32 station disconnected from AP");
		break;
	case SYSTEM_EVENT_STA_AUTHMODE_CHANGE:
		Serial.println("SYSTEM_EVENT_STA_AUTHMODE_CHANGE | the auth mode of AP connected by ESP32 station changed");
		break;
	case SYSTEM_EVENT_STA_LOST_IP:
		Serial.println("SYSTEM_EVENT_STA_LOST_IP | ESP32 station lost IP and the IP is reset to 0");
		break;
	case SYSTEM_EVENT_STA_WPS_ER_SUCCESS:
		Serial.println("SYSTEM_EVENT_STA_WPS_ER_SUCCESS | ESP32 station wps succeeds in enrollee mode");
		break;
	case SYSTEM_EVENT_STA_WPS_ER_FAILED:
		Serial.println("SYSTEM_EVENT_STA_WPS_ER_FAILED | ESP32 station wps fails in enrollee mode");
		break;
	case SYSTEM_EVENT_STA_WPS_ER_TIMEOUT:
		Serial.println("SYSTEM_EVENT_STA_WPS_ER_TIMEOUT | ESP32 station wps timeout in enrollee mode");
		break;
	case SYSTEM_EVENT_STA_WPS_ER_PIN:
		Serial.println("SYSTEM_EVENT_STA_WPS_ER_PIN | ESP32 station wps pin code in enrollee mode");
		break;
	case SYSTEM_EVENT_AP_START:
		Serial.println("SYSTEM_EVENT_AP_START | ESP32 soft-AP start");
		break;
	case SYSTEM_EVENT_AP_STOP:
		Serial.println("SYSTEM_EVENT_AP_STOP | ESP32 soft-AP stop");
		break;
	case SYSTEM_EVENT_AP_STACONNECTED:
		Serial.println("SYSTEM_EVENT_AP_STACONNECTED | a station connected to ESP32 soft-AP");
		break;
	case SYSTEM_EVENT_AP_STADISCONNECTED:
		Serial.println("SYSTEM_EVENT_AP_STADISCONNECTED | a station disconnected from ESP32 soft-AP");
		break;
	case SYSTEM_EVENT_AP_PROBEREQRECVED:
		Serial.println("SYSTEM_EVENT_AP_PROBEREQRECVED | Receive probe request packet in soft-AP interface");
		break;
	case SYSTEM_EVENT_GOT_IP6:
		Serial.println("SYSTEM_EVENT_GOT_IP6 | ESP32 station or ap or ethernet interface v6IP addr is preferred");
		break;
	case SYSTEM_EVENT_ETH_START:
		Serial.println("SYSTEM_EVENT_ETH_START | ESP32 ethernet start");
		break;
	case SYSTEM_EVENT_ETH_STOP:
		Serial.println("SYSTEM_EVENT_ETH_STOP | ESP32 ethernet stop");
		break;
	case SYSTEM_EVENT_ETH_CONNECTED:
		Serial.println("SYSTEM_EVENT_ETH_CONNECTED | ESP32 ethernet phy link up");
		break;
	case SYSTEM_EVENT_ETH_DISCONNECTED:
		Serial.println("SYSTEM_EVENT_ETH_DISCONNECTED | ESP32 ethernet phy link down");
		break;
	case SYSTEM_EVENT_ETH_GOT_IP:
		Serial.println("SYSTEM_EVENT_ETH_GOT_IP | ESP32 ethernet got IP from connected AP");
		break;
	}
}