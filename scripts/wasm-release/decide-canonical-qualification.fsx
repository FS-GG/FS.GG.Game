#load "CanonicalQualificationPolicy.fs"
open System
open System.IO
open System.Text.Json
open System.Text.Json.Serialization
open CanonicalQualificationPolicy

let args = fsi.CommandLineArgs |> Array.skip 1
if args.Length <> 2 then failwith "Usage: decide-canonical-qualification.fsx AUDIT.json OBSERVATION.json"
let options = JsonSerializerOptions(PropertyNameCaseInsensitive = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)
let audit = JsonSerializer.Deserialize<Audit>(File.ReadAllText args[0], options)
let observed = JsonSerializer.Deserialize<Observation>(File.ReadAllText args[1], options)
let result = decide audit observed
printfn "%s" (JsonSerializer.Serialize result)
if not result.Accepted then Environment.Exit 2
