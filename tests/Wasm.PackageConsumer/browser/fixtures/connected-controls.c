/* Guest controls, not host policy. Built with WASI34 in the consumer's private root. */
#include <stdint.h>
static uint32_t next = 4096, allocations, stage, mode;
static void descriptor(uint32_t d,uint32_t p,uint32_t n){uint32_t *v=(uint32_t *)(uintptr_t)d;v[0]=p;v[1]=n;}
uint32_t sc2c_abi_version(void){
#ifdef WRONG_VERSION
return 7;
#else
return 0x10000;
#endif
}
uint32_t sc2c_alloc(uint32_t n){
  allocations++;
#ifdef BAD_DESCRIPTOR
  if(allocations==1)return 0xfffffff0; for(;;){}
#endif
#ifdef BAD_INPUT
  if(allocations==2)return 4096;
#endif
  uint32_t p=next;next=(next+n+3)&~3u;return p;
}
void sc2c_free(uint32_t p,uint32_t n){
  (void)p;(void)n;
#ifdef BAD_INPUT
  for(;;){}
#endif
  if(mode==12 || (stage==2 && (mode==15 || mode==19)))for(;;){}
  if(stage==2 && (mode==16 || mode==17))__builtin_trap();
}
int32_t sc2c_initialize(uint32_t p,uint32_t n,uint32_t d){
  mode=n?*(uint8_t *)(uintptr_t)p:0;
  if(mode==10)for(;;){}
  stage=1;descriptor(d,0,0);return 0;
}
int32_t sc2c_process(uint32_t p,uint32_t n,uint32_t d){
  (void)p;(void)n;
  if(stage!=1)return 91;stage=2;
  if(mode==11)for(;;){}
  if(mode==17)__builtin_trap();
  if(mode==18)__builtin_wasm_memory_grow(0,1);
  uint8_t *out=(uint8_t *)(uintptr_t)8192;out[0]=1;out[1]=2;out[2]=3;out[3]=4;
  descriptor(d,(mode==15 || mode==19)?d:8192,4);return mode==19?92:0;
}
int32_t sc2c_shutdown(void){if(mode==14)for(;;){} return stage==2?203:204;}
