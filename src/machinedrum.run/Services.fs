/// Stand-ins for the firmware's sample services, written in the DSL and assembled into the
/// emulator's P memory. They follow the standard (MDX_MachineDumpStandard.txt, "Sample access"):
/// SAMPLE_INFO returns A1 length, B1 Q16.16 step, X1 $ffffff; SAMPLE_READ reads the frames at
/// the positions in Y:$00..$1F (the first N0 of them; others and out-of-range positions read 0)
/// into X:$00..$1F and returns A1 = 1. Both preserve X0, Y0, R6, R7, M6 and M7.
///
/// Each bank's sample lives in X memory; a table at `info` holds, per bank, its length, step and
/// base address.
module machinedrum.run.Services

open dsp56k
open dsp56k.dsl
open dsp56k.dsl.Asm

let info = 0x7f0

/// X: counters the stand-ins keep for the harness: SAMPLE_INFO calls, SAMPLE_READ calls, frames
/// requested (sum of N0, clamped to 32 per call).
let infoCalls = 0x7fc
let readCalls = 0x7fd
let readFrames = 0x7fe

let sampleInfo = [|
  label "SAMPLE_INFO"
  move (immL 0xffffff) m1
  move (xm (absL infoCalls)) a
  add (immL 1) a
  move a1 (xm (absL infoCalls))
  move x0 a                                     // 3 words per bank
  asl a
  add x0 a
  add (immL info) a
  move a1 r1
  nop
  move (xm (inc r1)) a                          // length
  move (xm (ind r1)) b                          // step
  move (immL 0xffffff) x1
  rts |]

let sampleRead = [|
  label "SAMPLE_READ"
  move (xm (absL readCalls)) a
  add (immL 1) a
  move a1 (xm (absL readCalls))
  move (xm (absL readFrames)) a
  move n0 x1
  add x1 a
  move a1 (xm (absL readFrames))
  move (immL 0xffffff) x1
  yield! [| for m in [| m1; m2; m3; m4 |] -> movec x1 m |]
  move x0 a
  asl a
  add x0 a
  add (immL info) a
  move a1 r1
  move n0 b                                     // frames still to read
  move (xm (inc r1)) y1                         // length
  update (inc r1)                               // (the step)
  move (xm (ind r1)) n2                         // base
  move (imm 0) r2                               // positions in Y:$00..
  move (imm 0) r3                               // frames to X:$00..
  doTo (imm 32) "SR_END"
  move (ym (inc r2)) a
  tst b
  ble "SR_ZERO"
  tst a
  blt "SR_ZERO"
  cmp y1 a
  bge "SR_ZERO"
  move n2 x1
  add x1 a
  move a1 r4
  nop
  move (xm (ind r4)) a
  bra "SR_STORE"
  label "SR_ZERO"
  clr a
  label "SR_STORE"
  move a (xm (inc r3))
  move (immL 1) x1
  sub x1 b
  label "SR_END"
  move (immL 1) a
  rts |]

/// The two services from `origin`, with their entry addresses.
let program origin = [| org Space.P origin; yield! sampleInfo; yield! sampleRead |]
