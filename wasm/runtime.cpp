#include "dsp56kEmu/dsp.h"
#include "dsp56kEmu/memory.h"
#include <memory>

// One isolated DSP per module instance; no audio devices, threads or JIT execution.
namespace {
using namespace dsp56k;
DefaultMemoryValidator validator;
PeripheralsNop peripheralX, peripheralY;
std::unique_ptr<Memory> memory;
std::unique_ptr<DSP> dsp;
uint32_t size = 0;
constexpr EReg registers[] = {
  Reg_R0, Reg_R1, Reg_R2, Reg_R3, Reg_R4, Reg_R5, Reg_R6, Reg_R7,
  Reg_N0, Reg_N1, Reg_N2, Reg_N3, Reg_N4, Reg_N5, Reg_N6, Reg_N7,
  Reg_M0, Reg_M1, Reg_M2, Reg_M3, Reg_M4, Reg_M5, Reg_M6, Reg_M7,
  Reg_X0, Reg_X1, Reg_Y0, Reg_Y1,
  Reg_A0, Reg_A1, Reg_A2, Reg_B0, Reg_B1, Reg_B2
};
bool valid(uint32_t bank, uint32_t address) {
  return dsp && bank < 3 && address < size;
}
}

extern "C" {
void dsp_destroy() { dsp.reset(); memory.reset(); size = 0; }
int dsp_create(uint32_t words) {
  if (words < 256 || words > 0x160000) return -1;
  dsp_destroy();
  try {
    memory = std::make_unique<Memory>(validator, words);
    dsp = std::make_unique<DSP>(*memory, &peripheralX, &peripheralY);
    size = words;
    for (unsigned i = 16; i < 24; ++i) dsp->writeReg(registers[i], TReg24(0xffffff));
    return 0;
  } catch (...) { dsp_destroy(); return -2; }
}
int dsp_write(uint32_t bank, uint32_t address, uint32_t word) {
  if (!valid(bank, address) || word > 0xffffff) return -1;
  if (!memory->set(static_cast<EMemArea>(bank), address, word)) return -2;
  if (bank == 0) dsp->clearOpcodeCache(address);
  return 0;
}
int dsp_read(uint32_t bank, uint32_t address) {
  return valid(bank, address) ? static_cast<int>(memory->get(static_cast<EMemArea>(bank), address)) : -1;
}
int dsp_reg_write(uint32_t reg, uint32_t word) {
  if (!dsp || reg >= std::size(registers) || word > 0xffffff) return -1;
  if (reg == 30 || reg == 33) {
    int64_t accumulator;
    const auto full = reg == 30 ? Reg_A : Reg_B;
    if (word > 0xff || !dsp->readRegToInt(full, accumulator)) return -1;
    const auto value = (static_cast<uint64_t>(accumulator) & 0xffffffffffffULL) |
      (static_cast<uint64_t>(word) << 48);
    return dsp->writeReg(full, TReg56(value)) ? 0 : -2;
  }
  return dsp->writeReg(registers[reg], TReg24(word)) ? 0 : -2;
}
int dsp_reg_read(uint32_t reg) {
  int64_t value;
  if (!dsp || reg >= std::size(registers) || !dsp->readRegToInt(registers[reg], value)) return -1;
  return static_cast<int>(value & 0xffffff);
}
// A nonreturning routine poisons the current call stack: recreate the DSP after a timeout.
int dsp_call(uint32_t entry, uint32_t limit) {
  if (!dsp || entry >= size - 1 || limit == 0 || limit > 10000000) return -1;
  const auto sentinel = size - 1;
  dsp->setPC(sentinel);
  dsp->jsr(entry);
  for (uint32_t i = 1; i <= limit; ++i) {
    if (dsp->getPC().toWord() >= size) return -3;
    dsp->execInterpreter();
    if (dsp->getPC().toWord() == sentinel) return static_cast<int>(i);
  }
  return -2;
}
}
