#include "fsgg-wasm-guest.h"
uint32_t fsgg_wasm_allocate(fsgg_wasm_arena *a, uint32_t n) {
  if (!a || !n) return 0;
  uint32_t start=(a->next+3u)&~3u;
  if (start>a->capacity || n>a->capacity-start) return 0;
  a->next=start+n; return (uint32_t)(uintptr_t)(a->bytes+start);
}
void fsgg_wasm_free_last(fsgg_wasm_arena *a,uint32_t p,uint32_t n){
  if(!a||!p||!n)return;uint32_t base=(uint32_t)(uintptr_t)a->bytes;
  if(p>=base&&p-base+n==a->next)a->next=p-base;
}
void fsgg_wasm_write_descriptor(uint32_t d,uint32_t o,uint32_t n){
  uint8_t *p=(uint8_t *)(uintptr_t)d;
  p[0]=(uint8_t)o;p[1]=(uint8_t)(o>>8);p[2]=(uint8_t)(o>>16);p[3]=(uint8_t)(o>>24);
  p[4]=(uint8_t)n;p[5]=(uint8_t)(n>>8);p[6]=(uint8_t)(n>>16);p[7]=(uint8_t)(n>>24);
}
