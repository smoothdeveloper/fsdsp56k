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
uint32_t bridge = 0;
constexpr EReg registers[] = {
  Reg_R0, Reg_R1, Reg_R2, Reg_R3, Reg_R4, Reg_R5, Reg_R6, Reg_R7,
  Reg_N0, Reg_N1, Reg_N2, Reg_N3, Reg_N4, Reg_N5, Reg_N6, Reg_N7,
  Reg_M0, Reg_M1, Reg_M2, Reg_M3, Reg_M4, Reg_M5, Reg_M6, Reg_M7,
  Reg_X0, Reg_X1, Reg_Y0, Reg_Y1,
  Reg_A0, Reg_A1, Reg_A2, Reg_B0, Reg_B1, Reg_B2
};
// What a snapshot holds after the 34 registers above: PC, SR, OMR, LA, LC, SP (not SSH/SSL: reading SSH pops the stack).
constexpr EReg control[] = { Reg_PC, Reg_SR, Reg_OMR, Reg_LA, Reg_LC, Reg_SP };
constexpr unsigned snapshotWords = std::size(registers) + std::size(control);
uint32_t snapshot[snapshotWords];
uint32_t stepSentinel = 0;
bool valid(uint32_t bank, uint32_t address) {
  return dsp && bank < 3 && address < size;
}
}

extern "C" {
double dsp_cycles() { return dsp ? static_cast<double>(dsp->getCycles()) : 0; }
double dsp_operations() { return dsp ? static_cast<double>(dsp->getInstructionCounter()) : 0; }
void dsp_destroy() { dsp.reset(); memory.reset(); size = 0; bridge = 0; }
int dsp_create(uint32_t words) {
  if (words < 256 || words > 0x180000) return -1;
  dsp_destroy();
  try {
    memory = std::make_unique<Memory>(validator, words);
    dsp = std::make_unique<DSP>(*memory, &peripheralX, &peripheralY);
    size = words;
    for (unsigned i = 16; i < 24; ++i) dsp->writeReg(registers[i], TReg24(0xffffff));
    return 0;
  } catch (...) { dsp_destroy(); return -2; }
}
int dsp_bridge(uint32_t address) {
  if (!dsp || address == 0 || address >= size) return -1;
  memory->setExternalMemory(address, true);
  bridge = address;
  return 0;
}
int dsp_write(uint32_t bank, uint32_t address, uint32_t word) {
  if (!valid(bank, address) || word > 0xffffff) return -1;
  if (!memory->set(static_cast<EMemArea>(bank), address, word)) return -2;
  if (bank == 0 || (bridge && address >= bridge)) dsp->clearOpcodeCache(address);
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
// Stepping, for a debugger: dsp_begin calls `entry` as dsp_call does, then each dsp_step runs one instruction and
// says whether the routine returned (1), is still running (0) or the PC left memory (-3).
int dsp_begin(uint32_t entry) {
  if (!dsp || entry >= size - 1) return -1;
  stepSentinel = size - 1;
  dsp->setPC(stepSentinel);
  dsp->jsr(entry);
  return 0;
}
int dsp_step() {
  if (!dsp || stepSentinel == 0) return -1;
  if (dsp->getPC().toWord() >= size) return -3;
  dsp->execInterpreter();
  if (dsp->getPC().toWord() == stepSentinel) { stepSentinel = 0; return 1; }
  return 0;
}
int dsp_pc() { return dsp ? static_cast<int>(dsp->getPC().toWord()) : -1; }
// Every register at once (the numbering of dsp_reg_read, then PC, SR, OMR, LA, LC, SP): dsp_snapshot takes it and
// returns how many words it has, dsp_snapshot_word reads one; a step's writes are what differs from the previous one.
int dsp_snapshot() {
  if (!dsp) return -1;
  unsigned i = 0;
  for (const auto r : registers) { int64_t v = 0; dsp->readRegToInt(r, v); snapshot[i++] = static_cast<uint32_t>(v & 0xffffff); }
  for (const auto r : control)   { int64_t v = 0; dsp->readRegToInt(r, v); snapshot[i++] = static_cast<uint32_t>(v & 0xffffff); }
  return static_cast<int>(snapshotWords);
}
int dsp_snapshot_word(uint32_t i) { return i < snapshotWords ? static_cast<int>(snapshot[i]) : -1; }
}

