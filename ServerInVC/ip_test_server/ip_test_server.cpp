// ip_test_server.cpp : Defines the entry point for the console application.
//

#include "stdafx.h"
#include <Winbase.h>
#include <conio.h>
#include <Iphlpapi.h>
#include <Iptypes.h>
#include "UdpNet.h"
#include "TcpNet.h"


DWORD WINAPI ThreadTcpEcho( LPVOID lpParam ) ;
DWORD WINAPI ThreadUdpEcho( LPVOID lpParam ) ;

void promptHelp()
{
	printf("Usage:\n");
	printf("ip_test_server [-iIP] [-p] [-h]\n");
	printf("-i: server IP (default IP = 192.168.1.62)\n");
	printf("-p: print error\n");
	printf("-h: help information\n\n");
	getch();
}


void ArpUpdate()
{
	unsigned char PhyAddr[6];
	ULONG PhyAddrLen = 6;
	SendARP(client_IP, server_IP, (PULONG)&PhyAddr, &PhyAddrLen);

	char serveraddr[20], clientaddr[20];
	char cmdArpUp[256]={0};
	sprintf_s(serveraddr, "%s",	inet_ntoa(*(struct in_addr *)&server_IP));
	sprintf_s(clientaddr, "%s",	inet_ntoa(*(struct in_addr *)&client_IP));
	sprintf_s(cmdArpUp, "arp -s %s %02x-%02x-%02x-%02x-%02x-%02x %s",	\
		clientaddr, PhyAddr[0], PhyAddr[1], \
		PhyAddr[2], PhyAddr[3], PhyAddr[4], PhyAddr[5], serveraddr);

	int iRes = WinExec(cmdArpUp, SW_HIDE);
	if ( iRes <= 31)
		printf("error arp update %d\n", iRes);

}

int _tmain(int argc, char* argv[])
{
	char *cmdLine = GetCommandLine();
	char *cmdStart, *cmdEnd, *cmdCur;
	cmdStart = cmdCur = strchr(cmdLine, ' ');
	cmdEnd = strchr(cmdLine,'\0');
	
	while (cmdCur < cmdEnd)
	{
		char *tmp = strchr(cmdCur, ' ');
		if (NULL == tmp)
		{
			tmp = cmdEnd;
		}
		char *curArg = new char[tmp - cmdCur];
		memcpy(curArg, cmdCur, tmp-cmdCur);
		
		switch(curArg[0])
		{
		case '-':
			if ((tmp-cmdCur) == 1)
			{
				promptHelp();
				return 0;
			}
			switch(curArg[1])
			{
			case 'h':
				promptHelp();
				return 0;

			case 'i':
				if ((tmp-cmdCur) == 2)
				{
					promptHelp();
					return 0;
				}
				server_IP = inet_addr(cmdCur+2);
				printf("%08x\n", server_IP);
				break;

			case 'p':
				bPrintErr = TRUE;
				break;

			default:
				break;
			}
			break;

		default:
			break;
		}

		cmdCur = tmp+1 ;
		delete []curArg;

	}

	//----------------------
	// Initialize Winsock.
	WSADATA wsaData;
	int iResult = WSAStartup(MAKEWORD(2,2), &wsaData);
	if (iResult != NO_ERROR) {
		printf("Error at WSAStartup()\n");
		return false;
	}

	fLog = fopen("ServerLog.txt", "w");
	if (bPrintErr)
	{
		fUdpErrLog = fopen("ServerUdpErrLog.txt", "w");
		fTcpErrLog = fopen("ServerTcpErrLog.txt", "w");
	}

	//SetArpCacheReg();

	ArpUpdate();

	HANDLE hUdp = CreateThread(	NULL, 0, ThreadUdpEcho,	0, 0, NULL);
	HANDLE hTcp = CreateThread(	NULL, 0, ThreadTcpEcho,	0, 0, NULL);
	
	DWORD tArpStart, tArpEnd;

	tArpStart = GetTickCount();

	while (1)
	{
		if(   _kbhit()   ) 
		{
			char key = getch();
		
			if ('q' == key || 'Q' == key)
			{
				TerminateThread(hTcp, 0);
				TerminateThread(hUdp, 0);
				break;
			}
		}
		
		tArpEnd = GetTickCount();
		if ( (tArpEnd - tArpStart) > 300000)
		{
			if (0 != client_IP)
			{
				/////////////////////////////////////////////////
				bPause = TRUE;
				if (tcpRunning)
				{
					while (!tcpStop)
						Sleep(10);
				}
				if (udpRunning)
				{
					while (!udpStop)
						Sleep(10);
				}

				//////////////////////////////////////////////////
	
				ArpUpdate();

				/////////////////////////////////////////////////////
				bPause = FALSE;


			}


			tArpStart = GetTickCount();
		}


		Sleep(1000);
	
	}

	//ResetArpCacheReg();

	fclose(fLog);
	if (bPrintErr)
	{
		fclose(fUdpErrLog);
		fclose(fTcpErrLog);
	}

	WSACleanup();


	return 0;
}

DWORD WINAPI ThreadTcpEcho( LPVOID lpParam ) 
{
	CTcpNet m_tcpServer;
	
	while (1)
	{
		if (true == m_tcpServer.NetInit())
			m_tcpServer.NetEcho();
		m_tcpServer.NetClose();
	}
	
	return 0;
}

DWORD WINAPI ThreadUdpEcho( LPVOID lpParam ) 
{
	CUdpNet m_udpServer;

	while (1)
	{
		if (true == m_udpServer.NetInit())
			m_udpServer.NetEcho();
		m_udpServer.NetClose();
	}

	return 0;

}