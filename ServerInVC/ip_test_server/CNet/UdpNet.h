#pragma once

#include "netdef.h"

class CUdpNet
{
public:
	CUdpNet(void);
public:
	~CUdpNet(void);

public:
	bool NetInit(void);
	void NetClose(void);
	void NetEcho();

private:
	SOCKET UDPsocket;

	int iLast;
	//int iCur;
};
