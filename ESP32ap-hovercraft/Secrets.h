// Secrets.h

#ifndef _SECRETS_h
#define _SECRETS_h

#if defined(ARDUINO) && ARDUINO >= 100
	#include "Arduino.h"
#else
	#include "WProgram.h"
#endif

//add this file to .gitignore so that your personal data is not shared
#define WIFI_SSID "#racing_replace_with_your_ssid"
#define WIFI_PASSWORD "replace_with_your_password" 

#endif