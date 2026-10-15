#include "trilink_app.h"
#include <stdio.h>
#include <string.h>

static unsigned checks, failures;
#define CHECK(x) do {++checks;if(!(x)){++failures;fprintf(stderr,"FAIL line %d: %s\n",__LINE__,#x);}}while(0)
typedef struct { unsigned received,ticks; uint64_t last_tick; bool accept; } module_state_t;
static bool receive(void *context,const trilink_route_view_t *message)
{
    module_state_t *state=context;
    CHECK(message->length==19U);
    ++state->received;
    return state->accept;
}
static void tick(void *context,uint64_t now)
{
    module_state_t *state=context;
    ++state->ticks;state->last_tick=now;
}
int main(void)
{
    trilink_app_dispatcher_t dispatcher;
    module_state_t states[TRILINK_APP_MAX_MODULES]={0};
    trilink_app_init(&dispatcher);
    CHECK(dispatcher.count==0U && trilink_app_capabilities(&dispatcher)==0U);
    CHECK(!trilink_app_register(NULL,NULL));
    trilink_app_module_t rgb={.capability_bit=TRILINK_APP_CAP_RGB,.marker=0xA6U,
        .version=1U,.wire_size=19U,.context=&states[0],.receive=receive,.tick=tick};
    CHECK(trilink_app_register(&dispatcher,&rgb));
    CHECK(trilink_app_capabilities(&dispatcher)==TRILINK_APP_CAP_RGB);
    CHECK(!trilink_app_register(&dispatcher,&rgb));
    trilink_app_module_t invalid=rgb;invalid.capability_bit=2U;
    CHECK(!trilink_app_register(&dispatcher,&invalid)); /* Duplicate wire marker. */
    invalid.marker=0xA7U;invalid.capability_bit=3U;
    CHECK(!trilink_app_register(&dispatcher,&invalid));
    invalid.capability_bit=2U;invalid.wire_size=TRILINK_ROUTE_DATA_MAX+1U;
    CHECK(!trilink_app_register(&dispatcher,&invalid));
    invalid.wire_size=1U;
    CHECK(!trilink_app_register(&dispatcher,&invalid));
    invalid.wire_size=19U;invalid.receive=NULL;
    CHECK(!trilink_app_register(&dispatcher,&invalid));
    CHECK(dispatcher.count==1U && trilink_app_capabilities(&dispatcher)==1U);
    uint8_t bytes[TRILINK_ROUTE_DATA_MAX+1U]={0xA6U,1U};
    trilink_route_view_t message={.data=bytes,.length=19U};
    CHECK(!trilink_app_receive(&dispatcher,&message) && states[0].received==1U);
    states[0].accept=true;
    CHECK(trilink_app_receive(&dispatcher,&message) && states[0].received==2U);
    bytes[1]=2U;CHECK(!trilink_app_receive(&dispatcher,&message));bytes[1]=1U;
    bytes[0]=0xA7U;CHECK(!trilink_app_receive(&dispatcher,&message));bytes[0]=0xA6U;
    message.length=18U;CHECK(!trilink_app_receive(&dispatcher,&message));
    message.length=20U;CHECK(!trilink_app_receive(&dispatcher,&message));
    message.length=TRILINK_ROUTE_DATA_MAX+1U;CHECK(!trilink_app_receive(&dispatcher,&message));
    message.length=1U;CHECK(!trilink_app_receive(&dispatcher,&message));
    message.length=19U;message.data=NULL;CHECK(!trilink_app_receive(&dispatcher,&message));
    CHECK(!trilink_app_receive(&dispatcher,NULL) && !trilink_app_receive(NULL,NULL));
    CHECK(states[0].received==2U); /* Malformed bodies never reached the handler. */
    for(unsigned i=1;i<TRILINK_APP_MAX_MODULES;++i) {
        trilink_app_module_t module=rgb;module.marker=(uint8_t)(0xA6U+i);
        module.capability_bit=(uint8_t)(1U<<i);module.context=&states[i];
        CHECK(trilink_app_register(&dispatcher,&module));
    }
    CHECK(dispatcher.count==TRILINK_APP_MAX_MODULES && trilink_app_capabilities(&dispatcher)==15U);
    invalid=rgb;invalid.marker=0xB0U;invalid.capability_bit=16U;
    CHECK(!trilink_app_register(&dispatcher,&invalid));
    trilink_app_tick(&dispatcher,5000U);
    for(unsigned i=0;i<TRILINK_APP_MAX_MODULES;++i)
        CHECK(states[i].ticks==1U && states[i].last_tick==5000U);
    trilink_app_init(&dispatcher);
    CHECK(dispatcher.count==0U && trilink_app_capabilities(&dispatcher)==0U);
    trilink_app_tick(&dispatcher,6000U);CHECK(states[0].ticks==1U);
    printf("trilink_app host tests: checks=%u failures=%u\n",checks,failures);
    return failures?1:0;
}
