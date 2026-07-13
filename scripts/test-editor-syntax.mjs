import { readFileSync } from "node:fs";
import { join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(fileURLToPath(new URL("..", import.meta.url)));

function read(path) {
  return readFileSync(join(root, path), "utf8");
}

function fail(message) {
  console.error(`[FAIL] ${message}`);
  process.exitCode = 1;
}

function assert(condition, message) {
  if (!condition) fail(message);
}

const grammarText = read("editor/textmate/code.tmLanguage.json");
let grammar;
try {
  grammar = JSON.parse(grammarText);
} catch (error) {
  fail(`TextMate grammar is not valid JSON: ${error.message}`);
}

const packageText = read("editor/vscode/package.json");
let extensionPackage;
try {
  extensionPackage = JSON.parse(packageText);
} catch (error) {
  fail(`VS Code package metadata is not valid JSON: ${error.message}`);
}

const languageConfigText = read("editor/vscode/language-configuration.json");
let languageConfiguration;
try {
  languageConfiguration = JSON.parse(languageConfigText);
} catch (error) {
  fail(`VS Code language configuration is not valid JSON: ${error.message}`);
}

assert(grammar?.scopeName === "source.code", "grammar scopeName must be source.code");
assert(Array.isArray(grammar?.fileTypes) && grammar.fileTypes.includes("code"), "grammar must register .code files");
assert(extensionPackage?.contributes?.languages?.[0]?.id === "code", "VS Code extension must contribute language id 'code'");
assert(extensionPackage?.contributes?.languages?.[0]?.extensions?.includes(".code"), "VS Code extension must contribute .code");
assert(extensionPackage?.contributes?.grammars?.[0]?.scopeName === "source.code", "VS Code extension must use source.code grammar scope");
assert(languageConfiguration?.comments?.lineComment === "//", "language configuration must define // comments");
assert(languageConfiguration?.comments?.blockComment?.[0] === "/*", "language configuration must define block comments");

const lexer = read("ConsoleApp1/Compiler/Lexer.cs");
const keywordMatches = [...lexer.matchAll(/\{\s*"([^"]+)"\s*,\s*TokenType\.[A-Za-z0-9_]+\s*\}/g)];
const lexerKeywords = keywordMatches.map(match => match[1]).sort();
assert(lexerKeywords.length > 0, "could not extract keywords from Lexer.cs");

const grammarSerialized = JSON.stringify(grammar);
for (const keyword of lexerKeywords) {
  assert(grammarSerialized.includes(keyword), `grammar is missing lexer keyword '${keyword}'`);
}

const builtinTypes = ["string", "map", "set", "queue", "stack"];
for (const typeName of builtinTypes) {
  assert(grammarSerialized.includes(typeName), `grammar is missing built-in type '${typeName}'`);
}

const requiredScopes = [
  "comment.line.double-slash.code",
  "comment.block.code",
  "string.quoted.double.code",
  "meta.interpolation.code",
  "constant.numeric.real.code",
  "keyword.control.code",
  "storage.type.primitive.code",
  "entity.name.function.code",
  "entity.name.type.code",
  "support.namespace.engine.code",
  "support.function.member.code"
];
for (const scope of requiredScopes) {
  assert(grammarSerialized.includes(scope), `grammar is missing required scope '${scope}'`);
}

const fixture = read("editor/fixtures/syntax_smoke.code");
for (const sample of [
  "package Examples.Editor;",
  "static public constant integer maxSpeed",
  "interface Actor",
  "implement Updatable {",
  "Input.keyIsDown",
  "Draw.clearScreen",
  "Colors.rgb",
  "literal braces \\{ok\\}"
]) {
  assert(fixture.includes(sample), `syntax fixture is missing '${sample}'`);
}

if (process.exitCode) {
  process.exit(process.exitCode);
}

console.log("[PASS] editor syntax grammar metadata and keyword drift checks");
