#!/usr/bin/env python3
"""Require the canonical negative receipt guards to kill a wrong-token mutant."""
import pathlib,subprocess,tempfile
root=pathlib.Path(__file__).resolve().parents[2]
model=(root/'eng/wasm-shared/lifecycle.qnt').read_text()
needle='c.host.freezeToken==token'
assert model.count(needle)==1
with tempfile.TemporaryDirectory(prefix='wasm-receipt-mutation-') as raw:
    work=pathlib.Path(raw)
    (work/'lifecycle.qnt').write_text(model.replace(needle,'true'))
    qualification=work/'compatible-qualification.qnt'
    qualification.write_bytes((root/'eng/wasm-shared/compatible-qualification.qnt').read_bytes())
    run=subprocess.run(['quint','test',str(qualification),'--main=compatibleQualificationTest','--match=frozenReceiptMustMatchTest','--seed=20261003'],stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True)
    assert run.returncode!=0 and 'Expect condition does not hold true' in run.stdout,run.stdout
    print('canonical wrong-token promotion mutation: REFUSED by actual receipt guard test')
