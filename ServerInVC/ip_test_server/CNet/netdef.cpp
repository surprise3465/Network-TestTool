#include "stdafx.h"
#include "netdef.h"

u_long server_IP = inet_addr("192.168.1.56");
u_short server_Port = htons(7);
u_long client_IP;

FILE *fLog = NULL;
FILE *fUdpErrLog = NULL;
FILE *fTcpErrLog = NULL;

BOOL bPrintErr = FALSE;

BOOL bPause = FALSE;
BOOL udpRunning = FALSE;
BOOL tcpRunning = FALSE;
BOOL udpStop = FALSE;
BOOL tcpStop = FALSE;