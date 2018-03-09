
#include "netdef.h"

class CTcpNet
{
public:
	CTcpNet(void);
public:
	~CTcpNet(void);
public:
	bool NetInit(void);
	void NetClose(void);
	void NetEcho();

private:
	SOCKET ListenSocket;
	SOCKET AcceptSocket;
	sockaddr_in	ClientAddr;

	int iLast;

};
