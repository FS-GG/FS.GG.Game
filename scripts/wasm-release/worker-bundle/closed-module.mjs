import { parse } from "acorn";
import { analyze } from "eslint-scope";

// A syntax check of the actual delivered artifact, not an import-regex claim.
export function validateClosedModule(source) {
  const ast=parse(source,{ecmaVersion:2022,sourceType:"module",ranges:true});
  const pending=[[ast,null]],parents=new Map();let nodes=0;
  const forbidden=new Set(["eval","Function","require","fetch","importScripts","XMLHttpRequest","WebSocket"]);
  while(pending.length){
    const [node,parent]=pending.pop();parents.set(node,parent);
    if(++nodes>2_000_000)throw new Error("Worker syntax node bound exceeded");
    if(node.type==="ImportDeclaration"||node.type==="ImportExpression"
       ||node.type==="ExportAllDeclaration"||node.type==="ExportNamedDeclaration"&&node.source)
      throw new Error("Worker artifact has an external module dependency");
    for(const value of Object.values(node)){
      if(Array.isArray(value)){
        for(const child of value)if(child&&typeof child.type==="string")pending.push([child,node]);
      }else if(value&&typeof value.type==="string")pending.push([value,node]);
    }
  }
  // The generated admission scanner legitimately binds a local `require`.
  // Resolve lexical references instead of treating its name as a Node import.
  const scope=analyze(ast,{ecmaVersion:2022,sourceType:"module",impliedStrict:true});
  const free=new Set(scope.globalScope.through.map(reference=>reference.identifier));
  for(const reference of free)if(forbidden.has(reference.name))
    throw new Error("Worker artifact references a dynamic execution/network primitive");
  // The pinned Fable Encoding input uses const g=globalThis; g.Buffer.
  // Track lexical const aliases, then confine EVERY use of a host-global root
  // or alias. Merely checking direct globalThis.fetch would miss an escape.
  const roots=new Map([...free].filter(node=>["globalThis","self","window"].includes(node.name)).map(node=>[node,node.name]));
  const variables=scope.scopes.flatMap(value=>value.variables),references=new Map();
  for(const value of scope.scopes)for(const reference of value.references)references.set(reference.identifier,reference.resolved);
  const aliases=new Map();let changed=true;
  while(changed){
    changed=false;
    for(const variable of variables){
      if(aliases.has(variable)||variable.defs.length!==1)continue;
      const definition=variable.defs[0],declaration=definition.node;
      if(definition.type!=="Variable"||declaration.id.type!=="Identifier"||parents.get(declaration)?.kind!=="const"
         ||declaration.init?.type!=="Identifier")continue;
      const root=roots.get(declaration.init)??aliases.get(references.get(declaration.init))?.root;
      if(root){aliases.set(variable,{root,declaration});changed=true;}
    }
  }
  const aliasDeclarations=new Set();
  for(const [variable,{root,declaration}] of aliases){
    aliasDeclarations.add(declaration);
    for(const reference of variable.references)roots.set(reference.identifier,root);
  }
  const allowed={globalThis:new Set(["Buffer"]),self:new Set(["location","postMessage","onmessage"]),window:new Set()};
  for(const [node,root] of roots){
    const parent=parents.get(node);
    if(parent?.type==="VariableDeclarator"&&aliasDeclarations.has(parent)
       &&(parent.init===node||parent.id===node))continue;
    const member=parent?.type==="MemberExpression"&&parent.object===node;
    const property=member?(parent.computed&&parent.property.type==="Literal"?parent.property.value:!parent.computed?parent.property.name:null):null;
    if(!member||!allowed[root].has(property))throw new Error("Worker host-global reference escapes its selected static members");
  }
  return {nodes,externalImports:0};
}
