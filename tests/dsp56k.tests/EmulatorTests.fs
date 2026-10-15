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
  ]
