#include "StdAfx.h"
#include "TcpNet.h"

CTcpNet::CTcpNet(void)
{
}

CTcpNet::~CTcpNet(void)
{

	NetClose();
}

bool CTcpNet::NetInit(void)
{

	//----------------------
	// Create a SOCKET for listening for
	// incoming connection requests.
	ListenSocket = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
	if (ListenSocket == INVALID_SOCKET) {
		printf("[TCP]--Error at socket(): %ld\n", WSAGetLastError());
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

	if (bind( ListenSocket, 
		(SOCKADDR*) &service, 
		sizeof(service)) == SOCKET_ERROR) {
			printf("bind() failed.\n");
			closesocket(ListenSocket);
			//WSACleanup();
			return false;
	}

	//----------------------
	// Listen for incoming connection requests.
	// on the created socket
	if (listen( ListenSocket, 1 ) == SOCKET_ERROR) {
		printf("[TCP]--Error listening on socket.\n");
		closesocket(ListenSocket);
		//WSACleanup();
		return false;
	}

	//----------------------
	// Create a SOCKET for accepting incoming requests.
	printf("[TCP]--Waiting for client to connect...\n");

	//----------------------
	// Accept the connection.
	int			ClientAddrLen = sizeof(ClientAddr);
	AcceptSocket = accept( ListenSocket, (sockaddr *)(&ClientAddr), &ClientAddrLen );
	if (AcceptSocket == INVALID_SOCKET) {
		printf("accept failed: %d\n", WSAGetLastError());
		closesocket(ListenSocket);
		//WSACleanup();
		return false;
	} else 
		printf("[TCP]--Client %d.%d.%d.%d connected.\n", ClientAddr.sin_addr.s_net,	\
													  ClientAddr.sin_addr.s_host,	\
													  ClientAddr.sin_addr.s_lh,	\
													  ClientAddr.sin_addr.s_impno);

	// No longer need server socket
	closesocket(ListenSocket);

	if (0 == client_IP)
		client_IP = ClientAddr.sin_addr.s_addr;


	int optval_buflen = 0xFFFFFFF;
	int optvalLen = sizeof(optval_buflen);
	//setsockopt(AcceptSocket, SOL_SOCKET, SO_RCVBUF, (char *)&optval_buflen, optvalLen );
	getsockopt(AcceptSocket, SOL_SOCKET, SO_RCVBUF, (char *)&optval_buflen, &optvalLen );

	printf("[TCP]--socket buf len = 0x%x\n", optval_buflen);

	iLast = 0;


	return true;
}

void CTcpNet::NetClose()
{
	closesocket(AcceptSocket);
	tcpRunning = FALSE;
}

void CTcpNet::NetEcho()
{
	char revbuf[65535];
	unsigned short revlength;
	revlength = 65535;

	while (1)
	{
		while (bPause)
		{
			tcpStop = TRUE;
			Sleep(10);
		}

		tcpStop = FALSE;

		tcpRunning = FALSE;
		int iResult = recv(AcceptSocket, revbuf, revlength, 0);
		if ( iResult > 0 )
		{

			tcpRunning = TRUE;
			SYSTEMTIME tNow;
			GetSystemTime(&tNow);


			int curSndLen = 0;

			while (curSndLen < iResult)
			{
				int tmpSndLen = send(AcceptSocket, revbuf+curSndLen, iResult-curSndLen, 0);
				if (tmpSndLen > 0)
					curSndLen += tmpSndLen;
				else if (SOCKET_ERROR == tmpSndLen)
				{
					printf("[TCP]--Connection from Client %d.%d.%d.%d error.\n", ClientAddr.sin_addr.s_net,	\
																					ClientAddr.sin_addr.s_host,	\
																					ClientAddr.sin_addr.s_lh,	\
																					ClientAddr.sin_addr.s_impno);

					return;

				}

			}

			fprintf(fLog, "%04d.%02d.%02d - %02d:%02d:%02d.%03d  [UDP]--Bytes received: 0x%x; Bytes sent: 0x%x\n", 
				tNow.wYear, tNow.wMonth, tNow.wDay, tNow.wHour+8, tNow.wMinute, tNow.wSecond, tNow.wMilliseconds, iResult, curSndLen);

			if (bPrintErr)
			{
				if (iResult != (iLast + 1) || iResult != curSndLen)
				{
					fprintf(fTcpErrLog, "%04d.%02d.%02d - %02d:%02d:%02d.%03d  [UDP]--last bytes = 0x%x, current rcv bytes = 0x%x, current snd bytes = 0x%x\n", 
						tNow.wYear, tNow.wMonth, tNow.wDay, tNow.wHour+8, tNow.wMinute, tNow.wSecond, tNow.wMilliseconds, iLast, iResult, curSndLen);

				}
				iLast = iResult;
			}

		}
		else if ( iResult == 0 )
		{
			printf("[TCP]--Connection from client %d.%d.%d.%d closed.\n", ClientAddr.sin_addr.s_net,	\
																	   ClientAddr.sin_addr.s_host,	\
																	   ClientAddr.sin_addr.s_lh,	\
																	   ClientAddr.sin_addr.s_impno);
			return;
		}
		else if ( SOCKET_ERROR == iResult )
		{
			printf("[TCP]--Connection from Client %d.%d.%d.%d error.\n", ClientAddr.sin_addr.s_net,	\
																	  ClientAddr.sin_addr.s_host,	\
																	  ClientAddr.sin_addr.s_lh,	\
																	  ClientAddr.sin_addr.s_impno);
			return;
		}

		//Sleep(8);
	}

}