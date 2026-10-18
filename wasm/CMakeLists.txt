cmake_minimum_required(VERSION 3.20)
project(dsp56300_wasm LANGUAGES C CXX)
set(CMAKE_CXX_STANDARD 17)
set(ASMJIT_STATIC ON CACHE BOOL "" FORCE)
set(ASMJIT_TEST OFF CACHE BOOL "" FORCE)
add_subdirectory(${UPSTREAM}/source/asmjit asmjit)
target_compile_options(asmjit PRIVATE -Wno-nontrivial-memcall)
file(GLOB BASE ${UPSTREAM}/source/dsp56kBase/*.cpp)
file(GLOB EMU ${UPSTREAM}/source/dsp56kEmu/*.cpp)
list(FILTER BASE EXCLUDE REGEX "(_test|win8sync|threadtools)\\.cpp$")
list(FILTER EMU EXCLUDE REGEX "(test[^/]*|dspthread)\\.cpp$")
add_executable(dsp56300 runtime.cpp ${BASE} ${EMU})
target_include_directories(dsp56300 PRIVATE ${UPSTREAM}/source)
target_link_libraries(dsp56300 PRIVATE asmjit)
# Upstream JIT headers require an emitter architecture even when never executed.
target_compile_definitions(dsp56300 PRIVATE DSP56K_FORCE_INTERPRETER=1 HAVE_X86_64=1 NDEBUG)
target_compile_options(dsp56300 PRIVATE -fconstexpr-steps=10000000 -fexceptions -Wno-nontrivial-memcall)
target_link_options(dsp56300 PRIVATE
  --no-entry -fexceptions
  -sMODULARIZE=1 -sEXPORT_ES6=1 -sENVIRONMENT=web,worker,node
  -sALLOW_MEMORY_GROWTH=1 -sINITIAL_MEMORY=134217728
  "-sEXPORTED_FUNCTIONS=['_dsp_create','_dsp_bridge','_dsp_destroy','_dsp_write','_dsp_read','_dsp_reg_write','_dsp_reg_read','_dsp_call','_dsp_cycles','_dsp_operations']")
set_target_properties(dsp56300 PROPERTIES SUFFIX ".mjs")
