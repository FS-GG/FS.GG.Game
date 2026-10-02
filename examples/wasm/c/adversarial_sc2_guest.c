#include "fsgg-wasm-guest.h"
static uint8_t storage[65536] __attribute__((aligned(4)));
static fsgg_wasm_arena arena={storage,sizeof(storage),0};
__attribute__((visibility("default"))) uint32_t sc2c_abi_version(void){return FSGG_WASM_SC2_ABI_VERSION;}
__attribute__((visibility("default"))) uint32_t sc2c_alloc(uint32_t n){return fsgg_wasm_allocate(&arena,n);}
__attribute__((visibility("default"))) void sc2c_free(uint32_t p,uint32_t n){fsgg_wasm_free_last(&arena,p,n);}
__attribute__((visibility("default"))) int32_t sc2c_initialize(uint32_t p,uint32_t n,uint32_t d){(void)p;(void)n;(void)d;return 0;}
__attribute__((visibility("default"))) int32_t sc2c_process(uint32_t p,uint32_t n,uint32_t d){(void)p;(void)n;(void)d;
#ifdef FSGG_WASM_TRAP
  __builtin_trap();
#elif defined(FSGG_WASM_LOOP)
  for(;;){}
#else
  return 31;
#endif
}
__attribute__((visibility("default"))) int32_t sc2c_shutdown(void){arena.next=0;return 0;}
