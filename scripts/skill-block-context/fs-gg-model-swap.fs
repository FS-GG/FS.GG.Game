// Typecheck fixtures for fs-gg-model-swap (see scripts/typecheck-md-blocks.fsx).

//#block 1
//#skip the Program.fs seam of the GENERATED product — it aliases `Product.Model.Model`, `Product.View.view`, `Product.EvidenceCommands.tick`, namespaces that exist only inside a scaffolded product (FS.GG.Templates owns them) and nowhere in this repo. Nothing here to compile it against.
