#ifndef FSGG_WASM_GUEST_H
#define FSGG_WASM_GUEST_H
#include <stdint.h>
#define FSGG_WASM_BAR_ABI_VERSION 1u
#define FSGG_WASM_SC2_ABI_VERSION 0x00010000u
#define FSGG_WASM_DESCRIPTOR_BYTES 8u
typedef struct { uint8_t *bytes; uint32_t capacity; uint32_t next; } fsgg_wasm_arena;
uint32_t fsgg_wasm_allocate(fsgg_wasm_arena *arena, uint32_t length);
void fsgg_wasm_free_last(fsgg_wasm_arena *arena, uint32_t pointer, uint32_t length);
void fsgg_wasm_write_descriptor(uint32_t descriptor, uint32_t output, uint32_t length);
#endif
