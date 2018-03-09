#include "StdAfx.h"
#include "UdpNet.h"


CUdpNet::CUdpNet(void)
{
}

CUdpNet::~CUdpNet(void)
{
}
bool CUdpNet::NetInit(void)
{

	//----------------------
	// Create a SOCKET for listening for
	// incoming connection requests.
	UDPsocket = socket(AF_INET,SOCK_DGRAM,0);
	if (UDPsocket == INVALID_SOCKET) {
		printf("[UDP]--Error at socket(): %ld\n", WSAGetLastError());
		//WSACleanup();
		return false;
	}

	//----------------------
	// The sockaddr_in structure specifies the address family,
	// IP address, and port for the socket that is being bound.
	sockaddr_in service;
	service.sin_family = AF_INET;
	service.sin_addr.s_addr = server_IP;
	service.sin_port = server_Port;

	if (bind( UDPsocket, 
		(SOCKADDR*) &service, 
		sizeof(service)) == SOCKET_ERROR) {
			printf("[UDP]--bind() failed.\n");
			closesocket(UDPsocket);
			//WSACleanup();
			return false;
	}

	printf("[UDP]--Start UDP communication...\n");

	int optval_buflen = 0x2000000;
	int optvalLen = sizeof(optval_buflen);
	setsockopt(UDPsocket, SOL_SOCKET, SO_RCVBUF, (char *)&optval_buflen, optvalLen );
	getsockopt(UDPsocket, SOL_SOCKET, SO_RCVBUF, (char *)&optval_buflen, &optvalLen );

	printf("[UDP]--socket buf len = 0x%x\n", optval_buflen);

	iLast = /*iCur = */0;

	
	return true;
}

void CUdpNet::NetClose()
{
	closesocket(UDPsocket);
	tcpRunning = FALSE;

}

void CUdpNet::NetEcho()
{
	sockaddr_in client;
	int			clientLen = sizeof(client);

	char revbuf[65535];
	unsigned short revlength;
	revlength = 65535;

	while (1)
	{
		while (bPause)
		{
			udpStop = TRUE;
			Sleep(10);
		}

		udpStop = FALSE;

		udpRunning = FALSE;	
		int iResult = recvfrom(	UDPsocket, revbuf, revlength, 0, (sockaddr *)(&client), &clientLen);
		if ( iResult > 0 )
		{

			udpRunning = TRUE;
			if (0 == client_IP)
				client_IP = client.sin_addr.s_addr;

			SYSTEMTIME tNow;
			GetSystemTime(&tNow);

			int curSndLen = 0;
			while (curSndLen < iResult)
			{
				int tmpSndLen = sendto(UDPsocket, revbuf+curSndLen, iResult-curSndLen, 0, (sockaddr *)(&client), clientLen);
				if (tmpSndLen > 0)
					curSndLen += tmpSndLen;
				else if (SOCKET_ERROR == tmpSndLen)
				{
					printf("[UDP]--Connection error.\n");
					return;
				}

			}

			fprintf(fLog, "%04d.%02d.%02d - %02d:%02d:%02d.%03d  [UDP]--Bytes received: 0x%x; Bytes sent: 0x%x\n", 
				tNow.wYear, tNow.wMonth, tNow.wDay, tNow.wHour+8, tNow.wMinute, tNow.wSecond, tNow.wMilliseconds, iResult, curSndLen);
			
			if (bPrintErr)
			{
				if (iResult != (iLast + 1) || iResult != curSndLen)
				{
					fprintf(fUdpErrLog, "%04d.%02d.%02d - %02d:%02d:%02d.%03d  [UDP]--last bytes = 0x%x, current rcv bytes = 0x%x, current snd bytes = 0x%x\n", 
						tNow.wYear, tNow.wMonth, tNow.wDay, tNow.wHour+8, tNow.wMinute, tNow.wSecond, tNow.wMilliseconds, iLast, iResult, curSndLen);

				}
				iLast = iResult;
			}

		}
		else if ( iResult == 0 )
		{
			printf("[UDP]--Connection closed.\n");
			return;
		}
		else if ( iResult == SOCKET_ERROR )
		{
			printf("[UDP]--Connection error.\n");
			return;
		}
	}

}