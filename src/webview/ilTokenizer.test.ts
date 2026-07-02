import { strict as assert } from "assert";
import { tokenizeIlText } from "./ilTokenizer";

const tokens = tokenizeIlText('IL_0007: callvirt instance string Sample.Widget::get_Name()');
assert.equal(tokens[0].kind, "offset");
assert.equal(tokens[2].kind, "opcode");
assert.equal(tokens[2].text, "callvirt");
assert.ok(tokens.some(token => token.kind === "signature"));

const literalTokens = tokenizeIlText('IL_0012: ldstr "abcd"');
assert.ok(literalTokens.some(token => token.kind === "literal" && token.text === '"abcd"'));

console.log("ilTokenizer tests passed");
