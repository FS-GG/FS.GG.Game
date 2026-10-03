/* Guest inputs only: independently built by pinned WASI SDK 34, no host policy. */
#include <stdint.h>
static uint32_t next=4096;
uint32_t sc2c_abi_version(void){return 0x10000;}
uint32_t sc2c_alloc(uint32_t n){uint32_t p=next;next=(next+n+3)&~3u;return p;}
void sc2c_free(uint32_t p,uint32_t n){(void)p;(void)n;}
static int32_t output(uint32_t p,uint32_t n,uint32_t d){uint32_t count=n?*(uint8_t *)(uintptr_t)p:0;if(count==255)for(;;){} uint8_t *out=(uint8_t *)(uintptr_t)32768;for(uint32_t i=0;i<count;i++)out[i]=(uint8_t)i;uint32_t *v=(uint32_t *)(uintptr_t)d;v[0]=count?32768:0;v[1]=count;return 0;}
int32_t sc2c_initialize(uint32_t p,uint32_t n,uint32_t d){return output(p,n,d);}
int32_t sc2c_process(uint32_t p,uint32_t n,uint32_t d){return output(p,n,d);}
int32_t sc2c_shutdown(void){return 0;}
