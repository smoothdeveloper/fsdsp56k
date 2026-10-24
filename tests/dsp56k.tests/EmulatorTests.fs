module dsp56k.tests.EmulatorTests

open Expecto
open dsp56k
open dsp56k.Native
open dsp56k.tests.Fixtures

let source = """
        org     p:$40
SUM:    clr     a
        move    #$10,r0
        do      #8,SUM_END
        move    y:(r0)+,x0
        add     x0,a
SUM_END:
        move    a,y:$20
        rts
"""

let intrinsicsAsm = """
        org     p:$40
INTR:   move    y:$10,a
        move    y:$11,x0
        cmp     x0,a
        tgt     x0,a
        move    a1,y:$20

        move    y:$10,a
        move    y:$11,x0
        cmp     x0,a
        tlt     x0,a
        move    a1,y:$21

        move    y:$12,a
        abs     a
        move    a,y:$22

        move    y:$13,a
        move    y:$14,a0
        move    a1,y:$23
        move    a0,y:$24
        rts
"""

let tests =
  testList "emulator" [
    test "an assembled routine runs and leaves its result in Y memory" {
      needNative ()
      let r = Assembler.assembleText Map.empty "sum.asm" source |> Result.defaultWith (fun e -> failwith (Assembler.errorText e))
      let org, words = Assembler.image dsp56k.Space.P r
      use emu = new Emulator(0x1000, 0x1000, 0x1000)
      emu.Load(Native.Space.P, org, words)
      emu.Load(Native.Space.Y, 0x10, [| 1 .. 8 |])
      let cycles = emu.Call(int r.symbols["SUM"], 0x10, 10000)
      Expect.isSome cycles "the routine returned"
      Expect.equal (emu.Read(Native.Space.Y, 0x20)) 36 "1 + ... + 8"
    }

    test "cmp + tlt/tgt, abs, and a1/a0 pack/unpack handle boundary values" {
      needNative ()
      let r = Assembler.assembleText Map.empty "intr.asm" intrinsicsAsm |> Result.defaultWith (fun e -> failwith (Assembler.errorText e))
      let org, words = Assembler.image dsp56k.Space.P r
      use emu = new Emulator(0x1000, 0x1000, 0x1000)
      emu.Load(Native.Space.P, org, words)
      let cases =
        [ // x, y, absIn, packHi, packLo, expectedMin, expectedMax, expectedAbs
          0x000000, 0x000000, 0x000000, 0x123456, 0xabcdef, 0x000000, 0x000000, 0x000000
          0x7fffff, 0x800000, 0x800000, 0x800000, 0x800001, 0x800000, 0x7fffff, 0x7fffff
          0xf00000, 0x100000, 0xf00000, 0x7fffff, 0xffffff, 0xf00000, 0x100000, 0x100000
          0x250000, 0x250000, 0x250000, 0x000000, 0x800000, 0x250000, 0x250000, 0x250000 ]
      for x, y, absIn, packHi, packLo, expMin, expMax, expAbs in cases do
        emu.Load(Native.Space.Y, 0x10, [| x; y; absIn; packHi; packLo |])
        let cycles = emu.Call(int r.symbols["INTR"], 0x10, 10000)
        Expect.isSome cycles "INTR returned"
        Expect.equal (emu.Read(Native.Space.Y, 0x20)) expMin $"min({x:x6}, {y:x6})"
        Expect.equal (emu.Read(Native.Space.Y, 0x21)) expMax $"max({x:x6}, {y:x6})"
        Expect.equal (emu.Read(Native.Space.Y, 0x22)) expAbs $"abs({absIn:x6})"
        Expect.equal (emu.Read(Native.Space.Y, 0x23)) packHi $"packHi({packHi:x6})"
        Expect.equal (emu.Read(Native.Space.Y, 0x24)) packLo $"packLo({packLo:x6})"
    }
  ]
