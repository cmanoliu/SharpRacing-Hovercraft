#include <WiFi.h>
#include "Secrets.h"

const char *ssid = "#racing";
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
/* Features: Support 480Hz high refresh rates */
#define DEFAULT_ESC_LEDC_FREQ            500 // 500 Hz
#define DEFAULT_ESC_LEDC_RESOLUTION_BITS  16 //  16 bits
#define DEFAULT_ESC_MAP_IN_MIN 0
#define DEFAULT_ESC_MAP_IN_MAX 100
#define DEFAULT_ESC_MAP_OUT_MIN 32767 // 1ms at 500 Hz / 16 bits 
#define DEFAULT_ESC_MAP_OUT_MAX 65535 // 2ms at 500 Hz / 16 bits 

#define DEFAULT_lift1_pulse_correction 0
#define DEFAULT_lift2_pulse_correction 0
#define DEFAULT_prop1_pulse_correction 0
#define DEFAULT_prop2_pulse_correction 0
#define DEFAULT_servo_pulse_correction 0

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

uint16_t lift1_pulse_correction = DEFAULT_lift1_pulse_correction;
uint16_t lift2_pulse_correction = DEFAULT_lift2_pulse_correction;

uint32_t prop_ledc_freq = DEFAULT_ESC_LEDC_FREQ;
uint8_t  prop_ledc_resolution_bits = DEFAULT_ESC_LEDC_RESOLUTION_BITS;
uint32_t prop_map_in_min = DEFAULT_ESC_MAP_IN_MIN;
uint32_t prop_map_in_max = DEFAULT_ESC_MAP_IN_MAX;
uint32_t prop_map_out_min = DEFAULT_ESC_MAP_OUT_MIN;
uint32_t prop_map_out_max = DEFAULT_ESC_MAP_OUT_MAX;

uint16_t prop1_pulse_correction = DEFAULT_prop1_pulse_correction;
uint16_t prop2_pulse_correction = DEFAULT_prop2_pulse_correction;

WiFiServer server(TCP_PORT);
IPAddress myIP;

unsigned int cnt=0;

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
  static unsigned int last = 0;
  static unsigned int delta = TICK_TIMEOUT;
  unsigned int now = millis();
  unsigned int diff = now - last;
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
  myIP = WiFi.softAPIP();
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
  Serial.print("); pulse correction: ");Serial.print(servo_pulse_correction); 
  Serial.println("); ");
#endif
}

void ledcSetup_LIFT(uint32_t freq, uint8_t resolution_bits, uint32_t map_in_min, uint32_t map_in_max, uint32_t map_out_min, uint32_t map_out_max, uint16_t pulse1_correction, uint16_t pulse2_correction) {
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
  Serial.print("); pulse corrections: ");Serial.print(lift1_pulse_correction); Serial.print(", "); Serial.print(lift2_pulse_correction);
  Serial.println(";");
#endif
}

void ledcSetup_PROP(uint32_t freq, uint8_t resolution_bits, uint32_t map_in_min, uint32_t map_in_max, uint32_t map_out_min, uint32_t map_out_max, uint16_t pulse1_correction, uint16_t pulse2_correction) {
  prop_ledc_freq = freq;
  prop_ledc_resolution_bits = resolution_bits;
  prop_map_in_min = map_in_min;
  prop_map_in_max = map_in_max;
  prop_map_out_min = map_out_min;
  prop_map_out_max = map_out_max;

  prop1_pulse_correction = pulse1_correction;
  prop2_pulse_correction = pulse2_correction;

  ledcSetup(PROP1_CHANNEL, prop_ledc_freq, prop_ledc_resolution_bits);
  ledcSetup(PROP2_CHANNEL, prop_ledc_freq, prop_ledc_resolution_bits);
  
#if defined(DEBUG)
  Serial.print("ledcSetup( PROP1_CHANNEL #"); Serial.print(PROP1_CHANNEL);  Serial.print(" and PROP2_CHANNEL #"); Serial.print(PROP2_CHANNEL); Serial.print(", ");
  Serial.print(prop_ledc_freq); Serial.print(", "); Serial.print(prop_ledc_resolution_bits); 
  Serial.print("); pulse corrections: ");Serial.print(prop1_pulse_correction); Serial.print(", "); Serial.print(prop2_pulse_correction);
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
    DEFAULT_lift1_pulse_correction,
    DEFAULT_lift2_pulse_correction);

  ledcSetup_PROP(
    DEFAULT_ESC_LEDC_FREQ, 
    DEFAULT_ESC_LEDC_RESOLUTION_BITS,
    DEFAULT_ESC_MAP_IN_MIN,
    DEFAULT_ESC_MAP_IN_MAX,
    DEFAULT_ESC_MAP_OUT_MIN,
    DEFAULT_ESC_MAP_OUT_MAX,
    DEFAULT_prop1_pulse_correction,
    DEFAULT_prop2_pulse_correction);

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
  int pulse = map(
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

  int finalPulse = pulse + servo_pulse_correction;
  ledcWrite(SERVO_CHANNEL, finalPulse);
  
#if defined(DEBUG)
  Serial.print("ledc wrote "); Serial.print(finalPulse); Serial.print(" to SERVO channel : ");
  Serial.print(SERVO_CHANNEL); Serial.println("; ");
#endif  
}

void write_LIFT(byte duty) {
  int pulse = map(
    duty, 
    lift_map_in_min,
    lift_map_in_max,
    lift_map_out_min,
    lift_map_out_max);

#if defined(DEBUG)
  Serial.print("map("); 
  Serial.print(duty); Serial.print(", ");
  Serial.print(lift_map_in_min); Serial.print(", ");
  Serial.print(lift_map_in_max); Serial.print(", ");
  Serial.print(lift_map_out_min); Serial.print(", ");
  Serial.print(lift_map_out_max); Serial.print("); ");
#endif  

  int pulse1 = pulse + lift1_pulse_correction;
  int pulse2 = pulse + lift2_pulse_correction;
  
  ledcWrite(LIFT1_CHANNEL, pulse1);
  ledcWrite(LIFT2_CHANNEL, pulse2);

#if defined(DEBUG)  
  Serial.print("ledc wrote "); Serial.print(pulse1); Serial.print(", "); Serial.print(pulse2); Serial.print(" to LIFT channels : ");
  Serial.print(LIFT1_CHANNEL); Serial.print(", ");Serial.print(LIFT2_CHANNEL); Serial.println("; ");
#endif    
}

void write_PROP(byte duty) {
  int pulse = map(
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

  int pulse1 = pulse + prop1_pulse_correction;
  int pulse2 = pulse + prop2_pulse_correction;

  ledcWrite(PROP1_CHANNEL, pulse1);
  ledcWrite(PROP2_CHANNEL, pulse2);
  
#if defined(DEBUG)  
  Serial.print("ledc wrote "); Serial.print(pulse1); Serial.print(", "); Serial.print(pulse2); Serial.print(" to PROP channels : ");
  Serial.print(PROP1_CHANNEL); Serial.print(", "); Serial.print(PROP2_CHANNEL); Serial.println("; ");
#endif   
}

void updateHovercraft(byte enabled, byte liftDuty, byte propDuty, byte servoDuty)
{
  if (enabled)
  {
    write_SERVO(servoDuty);
    write_LIFT(liftDuty);
    write_PROP(propDuty);
  }
  else
  {
    stopHovercraft();
  }
}

void stopHovercraft()
{
  ledcWrite(PROP1_CHANNEL, prop_map_out_min);
  ledcWrite(PROP2_CHANNEL, prop_map_out_min);

  ledcWrite(LIFT1_CHANNEL, lift_map_out_min);
  ledcWrite(LIFT2_CHANNEL, lift_map_out_min);

  ledcWrite(SERVO_CHANNEL, servo_map_out_min + (servo_map_out_max - servo_map_out_min) / 2);
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

#define SIZE_BUF_READ  256
#define SIZE_BUF_WRITE 640

uint8_t data[SIZE_BUF_READ]; 
char    buf[SIZE_BUF_WRITE];

WiFiClient client; 
int  tmp_len;

unsigned int last_packet_timestamp;
unsigned int isBrokenConnection;

uint32_t tmp_lift_freq;
byte     tmp_lift_resolution;
uint32_t tmp_lift_map_in_min;
uint32_t tmp_lift_map_in_max;
uint32_t tmp_lift_map_out_min;
uint32_t tmp_lift_map_out_max;

uint32_t tmp_propulsion_freq; 
byte     tmp_propulsion_resolution;
uint32_t tmp_propulsion_map_in_min; 
uint32_t tmp_propulsion_map_in_max; 
uint32_t tmp_propulsion_map_out_min;
uint32_t tmp_propulsion_map_out_max; 

uint32_t tmp_servo_freq;
byte     tmp_servo_resolution;
uint32_t tmp_servo_map_in_min;
uint32_t tmp_servo_map_in_max;
uint32_t tmp_servo_map_out_min;
uint32_t tmp_servo_map_out_max;

uint16_t tmp_lift1_pulse_correction;
uint16_t tmp_lift2_pulse_correction;
uint16_t tmp_prop1_pulse_correction;
uint16_t tmp_prop2_pulse_correction;
int16_t tmp_servo_pulse_correction;

byte tmp_enabled;
byte tmp_lift;
byte tmp_propulsion;
byte tmp_servo;

int tmp_offset;

#define CONTROL_PACKET_LENGTH 4
#define SETUP_PACKET_LENGTH 74

void serverPoll() {

  last_packet_timestamp = millis();
  
  /* listen for client */
  client = server.available(); 
  
  if (client) {                   
    Serial.print("New TCP client, IP address: ");
    Serial.print(client.remoteIP());
    Serial.print(":");
    Serial.println(client.remotePort());
          
    while (client.connected()) {    
       
      if (client.available()) {
        
        last_packet_timestamp = millis();

        // Read
        tmp_len = client.read(data, SIZE_BUF_READ);

#if defined(DEBUG)           
        Serial.print("Received ");
        Serial.print(tmp_len);
        Serial.println(" bytes.");
#endif 
        
        switch (tmp_len) {

          case CONTROL_PACKET_LENGTH:
          {
            tmp_enabled = data[0];
            if (tmp_enabled == 0 || tmp_enabled == 1)
            {
                tmp_lift = data[1];
                tmp_propulsion = data[2];
                tmp_servo = data[3];
    
                updateHovercraft(tmp_enabled, tmp_lift, tmp_propulsion, tmp_servo);
                sprintf(buf, "ACK#%u %u %u %u %u\r\n", cnt++, tmp_enabled, tmp_lift, tmp_propulsion, tmp_servo); 
                /* //char *bufp = buf;
                //bufp += sprintf(bufp, "%u", cnt++);
                //bufp += sprintf(bufp, "\r\n");
                buf[0] = 'A';
                buf[1] = 'C';
                buf[2] = 'K';
                buf[3] = '#';
                buf[4] = 13;
                buf[5] = 10;
                buf[6] = 0; */

                #if defined(DEBUG)          
                    Serial.println((char *)buf); 
                #endif 
            }
            else
            {
                stopHovercraft();
                sprintf(buf, "Invalid %u bytes (control) packet. The first byte is expected to be 00 (disabled) or 01 (enabled). STOP.\r\n", CONTROL_PACKET_LENGTH);
                Serial.println((char *)buf); 
            }
            break;
          }

          // setup packet 
          // 64 bytes =  1 + 3 x 21 bytes (4, 1, 4, 4, 4, 4 = uint32_t freq, uint8_t resolution_bits, uint32_t map_in_min, uint32_t map_in_max, uint32_t map_out_min, uint32_t map_out_max )
          // +8 bytes (4 words = lift1_pulse_correction, lift2_pulse_correction, prop1_pulse_correction, prop2_pulse_correction)
          // +2 bytes (1 word = servo_pulse_correction) 
          case SETUP_PACKET_LENGTH:
          {
            byte first = data[0];
            if (first == 2)
            {
                uint32_t i = 1;
                tmp_lift_freq = readInt32(data, 0 + i);         //  0,  1,  2,  3
                tmp_lift_resolution = data[4 + i];              //  4
                tmp_lift_map_in_min = readInt32(data, 5 + i);   //  5,  6,  7,  8
                tmp_lift_map_in_max = readInt32(data, 9 + i);   //  9, 10, 11, 12
                tmp_lift_map_out_min = readInt32(data, 13 + i); // 13, 14, 15, 16 
                tmp_lift_map_out_max = readInt32(data, 17 + i); // 17, 18, 19, 20 
    
                i = i + 21;
                tmp_propulsion_freq = readInt32(data, 0 + i);         //  0,  1,  2,  3 
                tmp_propulsion_resolution = data[4 + i];              //  4
                tmp_propulsion_map_in_min = readInt32(data, 5 + i);   //  5,  6,  7,  8 
                tmp_propulsion_map_in_max = readInt32(data, 9 + i);   //  9, 10, 11, 12 
                tmp_propulsion_map_out_min = readInt32(data, 13 + i); // 13, 14, 15, 16 
                tmp_propulsion_map_out_max = readInt32(data, 17 + i); // 17, 18, 19, 20 
    
                i = i + 21;
                tmp_servo_freq = readInt32(data, 0 + i);         //  0,  1,  2,  3 
                tmp_servo_resolution = data[4 + i];              //  4
                tmp_servo_map_in_min = readInt32(data, 5 + i);   //  5,  6,  7,  8 
                tmp_servo_map_in_max = readInt32(data, 9 + i);   //  9, 10, 11, 12 
                tmp_servo_map_out_min = readInt32(data, 13 + i); // 13, 14, 15, 16 
                tmp_servo_map_out_max = readInt32(data, 17 + i); // 17, 18, 19, 20 

                i = i + 21;
                tmp_lift1_pulse_correction = readUInt16(data, 0 + i);  //  0,  1 
                tmp_lift2_pulse_correction = readUInt16(data, 2 + i);  //  2,  3 
                tmp_prop1_pulse_correction = readUInt16(data, 4 + i);  //  4,  5
                tmp_prop2_pulse_correction = readUInt16(data, 6 + i);  //  6,  7
                tmp_servo_pulse_correction = readInt16(data, 8 + i);   //  8,  9

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
                  tmp_prop2_pulse_correction);

                ledcSetup_SERVO(
                  tmp_servo_freq, 
                  tmp_servo_resolution, 
                  tmp_servo_map_in_min,
                  tmp_servo_map_in_max,
                  tmp_servo_map_out_min,
                  tmp_servo_map_out_max,
                  tmp_servo_pulse_correction);

                sprintf(buf, "ACK#%u LIFT %u %u %u %u %u %u cx%u:%u | PROP %u %u %u %u %u %u cx%u:%u | SERVO %u %u %u %u %u %u cx%d\r\n", cnt++, 
                  tmp_lift_freq, tmp_lift_resolution, tmp_lift_map_in_min, tmp_lift_map_in_max, tmp_lift_map_out_min, tmp_lift_map_out_max, tmp_lift1_pulse_correction, tmp_lift2_pulse_correction,
                  tmp_propulsion_freq, tmp_propulsion_resolution, tmp_propulsion_map_in_min, tmp_propulsion_map_in_max, tmp_propulsion_map_out_min, tmp_propulsion_map_out_max, tmp_prop1_pulse_correction, tmp_prop2_pulse_correction,
                  tmp_servo_freq, tmp_servo_resolution, tmp_servo_map_in_min, tmp_servo_map_in_max, tmp_servo_map_out_min, tmp_servo_map_out_max, tmp_servo_pulse_correction
                ); 

                #if defined(DEBUG)          
                    Serial.println((char *)buf); 
                #endif 
            }
            else
            {
                stopHovercraft();
                sprintf(buf, "Invalid %u bytes (setup) packet. The first byte is expected to be 02. STOP.\r\n", SETUP_PACKET_LENGTH);
                Serial.println((char *)buf);
            }
            break;
          }

          //unexpected length packet
          default:
          {
            stopHovercraft();
            sprintf(buf, "Invalid packet. Expected a %u bytes (control) packet or an %u bytes (setup) packet. Received packet is %u bytes length. STOP.\r\n", CONTROL_PACKET_LENGTH, SETUP_PACKET_LENGTH, tmp_len);
            Serial.println((char *)buf);
            break;
          }
        }
        
        // Write
        tmp_len = strlen(buf);
        int rc = client.write(buf, tmp_len);
      } 

      isBrokenConnection = ((millis() - last_packet_timestamp) >= BROKEN_CONNECTION_TIMEOUT);
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
