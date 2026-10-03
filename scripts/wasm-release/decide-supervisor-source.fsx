#load "SupervisorSourcePolicy.fs"
open System
open System.IO
open System.Text.Json
open System.Text.Json.Serialization
open SupervisorSourcePolicy
let args = fsi.CommandLineArgs |> Array.skip 1
if args.Length <> 2 then failwith "Usage: decide-supervisor-source.fsx REVIEW.json OBSERVATION.json"
let options = JsonSerializerOptions(PropertyNameCaseInsensitive = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)
let result = decide (JsonSerializer.Deserialize<Review>(File.ReadAllText args[0], options)) (JsonSerializer.Deserialize<Observation>(File.ReadAllText args[1], options))
printfn "%s" (JsonSerializer.Serialize result)
if not result.Accepted then Environment.Exit 2
