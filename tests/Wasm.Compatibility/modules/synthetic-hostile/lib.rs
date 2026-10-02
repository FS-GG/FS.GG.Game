#![no_std]
use core::{panic::PanicInfo, ptr};
#[panic_handler] fn panic(_: &PanicInfo) -> ! { loop {} }
#[repr(align(4))] struct Arena([u8; 65536]);
static mut ARENA: Arena = Arena([0;65536]);
static mut NEXT: usize = 0;
static mut FREE_COUNT: u32 = 0;
static OUTPUT: [u8;4] = *b"TEST";
#[no_mangle] pub extern "C" fn sc2c_abi_version()->u32 { 0x0001_0000 }
#[no_mangle] pub extern "C" fn sc2c_alloc(length:u32)->u32 { unsafe { let start=(NEXT+3)&!3; let end=start+length as usize; if end>65536{return 0} NEXT=end;ptr::addr_of_mut!(ARENA.0).cast::<u8>().add(start) as u32 } }
#[no_mangle] pub extern "C" fn sc2c_free(_:u32,_:u32) { unsafe { FREE_COUNT+=1; } #[cfg(free_trap)] core::arch::wasm32::unreachable(); }
#[no_mangle] pub extern "C" fn test_free_count()->u32 { unsafe { FREE_COUNT } }
#[no_mangle] pub extern "C" fn sc2c_initialize(_:u32,_:u32,_:u32)->i32 { 0 }
#[no_mangle] pub extern "C" fn sc2c_process(input:u32,_:u32,descriptor:u32)->i32 {
    #[cfg(loop_forever)] loop {}
    #[cfg(call_trap)] core::arch::wasm32::unreachable();
    #[cfg(grow_memory)] unsafe { core::arch::wasm32::memory_grow::<0>(1); }
    unsafe {
        #[cfg(malformed_descriptor)] let output = input;
        #[cfg(not(malformed_descriptor))] let output = OUTPUT.as_ptr() as u32;
        ptr::write_unaligned(descriptor as *mut u32, output);
        ptr::write_unaligned((descriptor+4) as *mut u32, 4);
    }
    0
}
#[no_mangle] pub extern "C" fn sc2c_shutdown()->i32 { 0 }
