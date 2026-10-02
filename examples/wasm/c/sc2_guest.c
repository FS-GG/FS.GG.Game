#include "fsgg-wasm-guest.h"
static uint8_t storage[65536] __attribute__((aligned(4)));
static fsgg_wasm_arena arena={storage,sizeof(storage),0};
static const uint8_t output[4]={'S','C','2','C'};
__attribute__((visibility("default"))) uint32_t sc2c_abi_version(void){return FSGG_WASM_SC2_ABI_VERSION;}
__attribute__((visibility("default"))) uint32_t sc2c_alloc(uint32_t n){return fsgg_wasm_allocate(&arena,n);}
__attribute__((visibility("default"))) void sc2c_free(uint32_t p,uint32_t n){fsgg_wasm_free_last(&arena,p,n);}
static int32_t invoke(uint32_t p,uint32_t n,uint32_t d){if(!p||!n||!d)return 2;fsgg_wasm_write_descriptor(d,(uint32_t)(uintptr_t)output,sizeof(output));return 0;}
__attribute__((visibility("default"))) int32_t sc2c_initialize(uint32_t p,uint32_t n,uint32_t d){return invoke(p,n,d);}
__attribute__((visibility("default"))) int32_t sc2c_process(uint32_t p,uint32_t n,uint32_t d){return invoke(p,n,d);}
__attribute__((visibility("default"))) int32_t sc2c_shutdown(void){arena.next=0;return 0;}
