#include "trilink_web_policy.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static unsigned checks;
#define CHECK(expr) do { ++checks; if (!(expr)) { fprintf(stderr, "FAIL %s:%d %s\n", __FILE__, __LINE__, #expr); exit(1); } } while (0)
static bool allowed(const char *line, bool expected_mutation)
{
    bool mutation = !expected_mutation;
    return trilink_web_validate_line(line, strlen(line), &mutation) && mutation == expected_mutation;
}
int main(void)
{
    CHECK(trilink_web_allowed_host("192.168.240.1"));
    CHECK(trilink_web_allowed_host("192.168.240.1:80"));
    const char *hosts[] = {NULL,"","localhost","192.168.240.1.evil","192.168.240.1:8080","192.168.240.1 ","192.168.240.01","[::1]"};
    for (size_t i=0;i<sizeof(hosts)/sizeof(hosts[0]);++i) CHECK(!trilink_web_allowed_host(hosts[i]));
    CHECK(trilink_web_allowed_origin(NULL)); CHECK(trilink_web_allowed_origin(""));
    CHECK(trilink_web_allowed_origin("http://192.168.240.1")); CHECK(trilink_web_allowed_origin("http://192.168.240.1:80"));
    const char *origins[] = {"null","https://192.168.240.1","http://192.168.240.1/","http://192.168.240.1:8080","http://evil","http://192.168.240.1.evil"};
    for (size_t i=0;i<sizeof(origins)/sizeof(origins[0]);++i) CHECK(!trilink_web_allowed_origin(origins[i]));
    CHECK(trilink_web_local_ipv4(0xc0a8f001,0xc0a8f002));
    CHECK(trilink_web_local_ipv4(0xc0a8f001,0xc0a8f003));
    CHECK(!trilink_web_local_ipv4(0xc0a8f001,0xc0a8f004));
    CHECK(!trilink_web_local_ipv4(0xc0a80001,0xc0a8f002));
    CHECK(!trilink_web_local_ipv4(0xc0a8f001,0xc0a80002));
    CHECK(!trilink_web_local_ipv4(0xc0a8f001,0xc0a8f000));
    CHECK(!trilink_web_local_ipv4(0xc0a8f001,0xc0a8f001));
    CHECK(!trilink_web_local_ipv4(0xc0a8f001,0xc0a8f0ff));
    const char *token="1234567890abcdef1234567890abcdef";
    CHECK(trilink_web_token_equal(token,token));
    CHECK(!trilink_web_token_equal(token,"1234567890abcdef1234567890abcdee"));
    CHECK(!trilink_web_token_equal(token,"1234567890abcdef1234567890abcde"));
    CHECK(!trilink_web_token_equal(NULL,token)); CHECK(!trilink_web_token_equal(token,NULL));
    uint64_t deadline = 0;
    CHECK(trilink_web_deadline("2600",100,&deadline)); CHECK(deadline==2600);
    CHECK(trilink_web_deadline("5100",100,&deadline));
    CHECK(!trilink_web_deadline("5101",100,&deadline));
    CHECK(!trilink_web_deadline("100",100,&deadline));
    CHECK(!trilink_web_deadline("99",100,&deadline));
    CHECK(!trilink_web_deadline("0",100,&deadline));
    CHECK(!trilink_web_deadline("-1",100,&deadline));
    CHECK(!trilink_web_deadline("+2600",100,&deadline));
    CHECK(!trilink_web_deadline("2600 ",100,&deadline));
    CHECK(!trilink_web_deadline("18446744073709551616",100,&deadline));
    CHECK(!trilink_web_deadline(NULL,100,&deadline));
    CHECK(!trilink_web_deadline("2600",100,NULL));
    CHECK(allowed("TRILINK/1 HELLO nonce\n",false)); CHECK(allowed("TRILINK/1 SEARCH nonce\n",false));
    CHECK(allowed("TRILINK/3 CAPS nonce\n",false)); CHECK(allowed("TRILINK/3 ROOMGET n 00\n",false));
    CHECK(allowed("TRILINK/3 ROOMGET n 12\n",false)); CHECK(allowed("TRILINK/3 RGBRESULT n\n",false));
    CHECK(allowed("TRILINK/3 RGBENABLE n 1\n",true)); CHECK(allowed("TRILINK/3 RGBENABLE n 0\n",true));
    CHECK(allowed("TRILINK/3 RGB n 98C377B2D0D4 000101\n",true));
    char room[320]="TRILINK/3 ROOM n "; size_t prefix=strlen(room); memset(room+prefix,'0',264); room[prefix+264]='\n'; room[prefix+265]=0;
    CHECK(allowed(room,true));
    const char *bad[]={"", "TRILINK/1 HELLO n", "TRILINK/1 HELLO n\r\n", "TRILINK/1 HELLO n\n\n",
        "TRILINK/1 HELLO n\nTRILINK/3 RGBENABLE n 1\n", "TRILINK/1 HELLO  n\n", "TRILINK/3 CAPS n \n",
        "TRILINK/3 CAPS n\t\n", "TRILINK/3 CAPS n extra\n", "TRILINK/3 CONFIG n 00000001 00000001 98C377B2D0D4\n",
        "TRILINK/3 RELAY n 1\n", "TRILINK/3 SEND n 98C377B2D0D4 01\n", "TRILINK/3 POLL n\n", "TRILINK/3 TABLE n\n",
        "TRILINK/3 INFO n\n", "TRILINK/2 CAPS n\n", "TRILINK/3 ROOMGET n 13\n", "TRILINK/3 RGBENABLE n 2\n",
        "TRILINK/3 CAPS ................................\n", "TRILINK/3 CAPS n\x80\n", "TRILINK/3 ROOM n 00\n"};
    bool mutation;
    for(size_t i=0;i<sizeof(bad)/sizeof(bad[0]);++i) CHECK(!trilink_web_validate_line(bad[i],strlen(bad[i]),&mutation));
    char nul[]="TRILINK/3 CAPS n\0injected\n";
    CHECK(!trilink_web_validate_line(nul,sizeof(nul)-1,&mutation));
    char oversized[385]; memset(oversized,'A',sizeof(oversized)); oversized[383]='\n'; oversized[384]=0;
    CHECK(!trilink_web_validate_line(oversized,384,&mutation));
    CHECK(!trilink_web_validate_line("a\n",2,NULL));
    const char *command="TRILINK/3 CAPS n\n", *reply="\nTRILINK/3 CAPS n 1 0.16.0 00000001 84 06 06 01\n";
    trilink_web_mailbox_t box={0}; char line[384], output[2048]; uint32_t first=0, second=0; size_t length;
    CHECK(trilink_web_enqueue(&box,command,strlen(command),100,&first)); CHECK(first==1); CHECK(box.state==WEB_QUEUED);
    CHECK(!trilink_web_enqueue(&box,command,strlen(command),100,&second));
    CHECK(!trilink_web_dequeue(&box,101,line,2,&second)); CHECK(box.state==WEB_QUEUED);
    CHECK(trilink_web_dequeue(&box,101,line,sizeof(line),&second)); CHECK(second==first); CHECK(!strcmp(line,command));
    CHECK(!trilink_web_dequeue(&box,101,line,sizeof(line),&second));
    CHECK(trilink_web_collect(&box,first,101,output,sizeof(output),&length)==0);
    trilink_web_finish(&box,first+1,reply,strlen(reply)); CHECK(box.state==WEB_RUNNING);
    trilink_web_finish(&box,first,reply,strlen(reply)); CHECK(box.state==WEB_DONE);
    CHECK(trilink_web_collect(&box,first,102,output,sizeof(output),&length)==1); CHECK(!strcmp(output,reply)); CHECK(length==strlen(reply));
    CHECK(box.state==WEB_IDLE); CHECK(trilink_web_collect(&box,first,102,output,sizeof(output),&length)==-1);
    CHECK(trilink_web_enqueue(&box,command,strlen(command),100,&first));
    CHECK(!trilink_web_dequeue(&box,2300,line,sizeof(line),&second)); CHECK(box.state==WEB_IDLE);
    CHECK(trilink_web_enqueue(&box,command,strlen(command),100,&first));
    CHECK(trilink_web_collect(&box,first,2300,output,sizeof(output),&length)==-1); CHECK(box.state==WEB_IDLE);
    CHECK(trilink_web_enqueue(&box,command,strlen(command),100,&first)); CHECK(trilink_web_dequeue(&box,101,line,sizeof(line),&second));
    CHECK(trilink_web_collect(&box,first,2300,output,sizeof(output),&length)==-1); CHECK(box.state==WEB_ABANDONED);
    CHECK(!trilink_web_enqueue(&box,command,strlen(command),2400,&second));
    trilink_web_finish(&box,first,reply,strlen(reply)); CHECK(box.state==WEB_IDLE);
    CHECK(trilink_web_enqueue(&box,command,strlen(command),2500,&second)); CHECK(second>first);
    trilink_web_finish(&box,first,reply,strlen(reply)); CHECK(box.state==WEB_QUEUED);
    trilink_web_abandon(&box,first); CHECK(box.state==WEB_QUEUED);
    trilink_web_abandon(&box,second); CHECK(box.state==WEB_IDLE);
    CHECK(trilink_web_enqueue(&box,command,strlen(command),1,&first)); CHECK(trilink_web_dequeue(&box,2,line,sizeof(line),&second));
    trilink_web_abandon(&box,first); CHECK(box.state==WEB_ABANDONED);
    trilink_web_finish(&box,first,reply,strlen(reply)); CHECK(box.state==WEB_IDLE);
    CHECK(trilink_web_enqueue(&box,command,strlen(command),1,&first)); CHECK(trilink_web_dequeue(&box,2,line,sizeof(line),&second));
    trilink_web_finish(&box,first,NULL,0); CHECK(trilink_web_collect(&box,first,3,output,sizeof(output),&length)==1);
    CHECK(strstr(output,"UNKNOWN")!=NULL);
    box.sequence=UINT32_MAX; CHECK(!trilink_web_enqueue(&box,command,strlen(command),1,&first));
    box.sequence=0; CHECK(!trilink_web_enqueue(&box,command,strlen(command),UINT64_MAX,&first));
    printf("PASS web policy/mailbox: %u checks; mailbox=%u bytes\n",checks,(unsigned)sizeof(box));
    return 0;
}
