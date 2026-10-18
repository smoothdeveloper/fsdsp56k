/// A kernel that no quotation described: Sine written by hand as a tree (Ir.Build), compiled by the same generator, gives
/// the program the quotation front end gives for Sine.fs. The back end reads the tree, not where it came from.
module dsp56k.tests.IrFrontEndTests

open Expecto
open dsp56k
open dsp56k.dsl
open dsp56k.dsl.Asm
open dsp56k.kernel
open dsp56k.kernel.Ir
open dsp56k.kernel.Ir.Patterns
open dsp56k.kernel.Ir.Build

/// sine.asm56k as a tree: what tests/dsp56k.tests/Kernels/Sine.fs says, with no F# behind it.
let sine : IrKernel =
  // read the quotation front end once so the registry of intrinsics is filled
  FromQuotation.kernel samples.kernels.Sine.kernel |> ignore
  let owner = "ByHand.Sine"
  let paramsTy, stateTy = TyRecord(owner + "+Params"), TyRecord(owner + "+State")
  let pitch, amp = property owner "pitch" TyFrac false, property owner "amp" TyFrac false
  let phase, ampPrev = property owner "phase" TyLong true, property owner "ampPrev" TyFrac true
  let p, s, out = Var("p", paramsTy), Var("s", stateTy), Var("out", TyOutput)
  let unitArg = Var("_arg1", paramsTy)
  let acc (x: Expr) = call "acc.ofFrac" [ x ]
  let step = Var("step", TyAcc)
  let ramp = Var("ramp", TyAcc)
  let gain = Var("gain", TyAcc, true)
  let ph = Var("phase", TyAcc, true)
  let i = Var("forLoopVar", TyInt)
  let v = Var("v", TyFrac)
  let shiftBy (n: int) (x: Expr) = operator "op_RightShift" TyAcc [ x; Expr.Value n ]
  let hi (x: Expr) = call "acc.hi" [ x ]
  let produced = shiftBy 2 (operator "op_Multiply" TyAcc [ Expr.Var v; hi (Expr.Var gain) ])
  let nextPhase = call "acc.andHi" [ Expr.Value 0x7fff; operator "op_Addition" TyAcc [ Expr.Var ph; Expr.Var step ] ]
  let sample =
    Expr.Sequential(
      callOn (Expr.Var out) "out.write" [ call "acc.sat" [ produced ] ],
      Expr.VarSet(ph, nextPhase))
  let passBody =
    Expr.Sequential(
      Expr.VarSet(gain, operator "op_Addition" TyAcc [ Expr.Var gain; Expr.Var ramp ]),
      Expr.Let(v, call "table.read" [ constant (box Rom.sine) TyTable; hi (Expr.Var ph) ], sample))
  let loop = Expr.ForIntegerRangeLoop(i, Expr.Value 0, Expr.Value 31, passBody)
  let tail = Expr.Sequential(write s phase (call "long.ofAcc" [ Expr.Var ph ]), write s ampPrev (read p amp))
  let body =
    Expr.Let(step, shiftBy 6 (acc (read p pitch)),
      Expr.Let(ramp, shiftBy 5 (operator "op_Subtraction" TyAcc [ acc (read p amp); acc (read s ampPrev) ]),
        Expr.Let(gain, acc (read s ampPrev),
          Expr.Let(ph, call "acc.ofLong" [ read s phase ], Expr.Sequential(loop, tail)))))
  let render = Expr.Lambda(p, Expr.Lambda(s, Expr.Lambda(out, body)))
  let init = Expr.Lambda(unitArg, Expr.Lambda(s, Expr.Sequential(write s phase (constant (box Long.zero) TyLong), write s ampPrev (constant (box Frac.one) TyFrac))))
  let trig = Expr.Lambda(unitArg, Expr.Lambda(s, write s phase (constant (box Long.zero) TyLong)))
  { name = "sine-by-hand"; paramsType = paramsTy; stateType = stateTy
    parameters = [| field "pitch" TyFrac; field "amp" TyFrac |]
    state = Some [| field "phase" TyLong; field "ampPrev" TyFrac |]
    init = func owner "init" TyUnit 2 [] init; trig = func owner "trig" TyUnit 2 [] trig; render = func owner "render" TyUnit 3 [] render }

/// The items without the comments that quote the F# source.
let code (items: Item array) = items |> Array.filter (fun it -> match it with Comment _ -> false | _ -> true)

let tests =
  testList "kernel IR, a second front end" [
    for target in Kernel.hostTargets do
      test $"Sine by hand compiles as Sine.fs does ({target.name})" {
        let byHand = Compile.generateIr target sine
        let quoted = Compile.generate target samples.kernels.Sine.kernel
        match byHand, quoted with
        | Ok h, Ok q ->
          Expect.equal (Program.source (code h.items)) (Program.source (code q.items)) "the same program"
          Expect.equal h.layout q.layout "the same layout"
        | Error e, _ -> failtest $"by hand: {e}"
        | _, Error e -> failtest $"quoted: {e}"
      }
  ]
