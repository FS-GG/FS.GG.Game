import { parse } from "acorn";
import { analyze } from "eslint-scope";

// A syntax check of the actual delivered artifact, not an import-regex claim.
export function validateClosedModule(source) {
  const ast=parse(source,{ecmaVersion:2022,sourceType:"module",ranges:true});
  const pending=[ast],members=[];let nodes=0;
  const forbidden=new Set(["eval","Function","require","fetch","importScripts","XMLHttpRequest","WebSocket"]);
  while(pending.length){
    const node=pending.pop();
    if(++nodes>2_000_000)throw new Error("Worker syntax node bound exceeded");
    if(node.type==="ImportDeclaration"||node.type==="ImportExpression"
       ||node.type==="ExportAllDeclaration"||node.type==="ExportNamedDeclaration"&&node.source)
      throw new Error("Worker artifact has an external module dependency");
    if(node.type==="MemberExpression")members.push(node);
    for(const value of Object.values(node)){
      if(Array.isArray(value)){
        for(const child of value)if(child&&typeof child.type==="string")pending.push(child);
      }else if(value&&typeof value.type==="string")pending.push(value);
    }
  }
  // The generated admission scanner legitimately binds a local `require`.
  // Resolve lexical references instead of treating its name as a Node import.
  const scope=analyze(ast,{ecmaVersion:2022,sourceType:"module",impliedStrict:true});
  const free=new Set(scope.globalScope.through.map(reference=>reference.identifier));
  for(const reference of free)if(forbidden.has(reference.name))
    throw new Error("Worker artifact references a dynamic execution/network primitive");
  for(const node of members)if(free.has(node.object)&&["globalThis","self","window"].includes(node.object.name)
    &&forbidden.has(node.computed?node.property?.value:node.property?.name))
      throw new Error("Worker artifact references a dynamic global primitive");
  return {nodes,externalImports:0};
}
