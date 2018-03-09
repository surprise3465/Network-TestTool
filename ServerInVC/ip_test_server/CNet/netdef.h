#pragma once

extern u_long server_IP ;
extern u_short server_Port;
extern u_long client_IP;

extern FILE *fLog;
extern FILE *fUdpErrLog;
extern FILE *fTcpErrLog;

extern BOOL bPrintErr;

extern BOOL bPause ;
extern BOOL udpRunning;
extern BOOL tcpRunning;
extern BOOL udpStop;
extern BOOL tcpStop;
