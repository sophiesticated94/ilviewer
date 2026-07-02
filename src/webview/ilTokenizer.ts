export type IlTokenKind = "offset" | "opcode" | "operand" | "literal" | "number" | "signature" | "target" | "punctuation" | "text";

export interface IlToken {
  kind: IlTokenKind;
  text: string;
}

export function tokenizeIlText(text: string): IlToken[] {
  const header = /^(IL_[0-9a-fA-F]{4}:)(\s+)([a-zA-Z0-9_.]+)(.*)$/.exec(text);
  if (!header) {
    return [{ kind: "text", text }];
  }

  const tokens: IlToken[] = [
    { kind: "offset", text: header[1] },
    { kind: "text", text: header[2] },
    { kind: "opcode", text: header[3] }
  ];
  const operand = header[4];
  let index = 0;

  while (index < operand.length) {
    const rest = operand.slice(index);
    const stringMatch = /^"([^"\\]|\\.)*"/.exec(rest);
    if (stringMatch) {
      tokens.push({ kind: "literal", text: stringMatch[0] });
      index += stringMatch[0].length;
      continue;
    }

    const targetMatch = /^IL_[0-9a-fA-F]{4}/.exec(rest);
    if (targetMatch) {
      tokens.push({ kind: "target", text: targetMatch[0] });
      index += targetMatch[0].length;
      continue;
    }

    const numberMatch = /^[-+]?\d+(\.\d+)?/.exec(rest);
    if (numberMatch) {
      tokens.push({ kind: "number", text: numberMatch[0] });
      index += numberMatch[0].length;
      continue;
    }

    const signatureMatch = /^([A-Za-z_][A-Za-z0-9_`.[\],:&*<>?+-]*::|[A-Za-z_][A-Za-z0-9_`.[\],:&*<>?+-]*\()/u.exec(rest);
    if (signatureMatch) {
      tokens.push({ kind: "signature", text: signatureMatch[0] });
      index += signatureMatch[0].length;
      continue;
    }

    const punctuationMatch = /^[()[\]{},:=<>+*/.-]/.exec(rest);
    if (punctuationMatch) {
      tokens.push({ kind: "punctuation", text: punctuationMatch[0] });
      index += punctuationMatch[0].length;
      continue;
    }

    const wordMatch = /^[^\s()[\]{},:=<>+*/.-]+/.exec(rest);
    if (wordMatch) {
      tokens.push({ kind: "operand", text: wordMatch[0] });
      index += wordMatch[0].length;
      continue;
    }

    tokens.push({ kind: "text", text: operand[index] });
    index++;
  }

  return tokens;
}
