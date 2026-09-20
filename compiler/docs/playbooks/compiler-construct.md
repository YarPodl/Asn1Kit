# Плейбук: новая конструкция ASN.1 в компиляторе

Расширение профиля X.680. Границы текущего профиля — в [docs/status.md](../status.md).

## Порядок

1. **Лексер.** [src/Asn1Kit.Compiler/Asn1Lexer.cs](../../src/Asn1Kit.Compiler/Asn1Lexer.cs): новое ключевое слово или форма литерала. Токены уже есть для `bstring` / `hstring` / `cstring`, диапазона `..` и `...`.
2. **AST.** [src/Asn1Kit.Compiler/Ast.cs](../../src/Asn1Kit.Compiler/Ast.cs): узел с `Line` / `Column` — позиция нужна для сообщений об ошибках.
3. **Парсер.** [src/Asn1Kit.Compiler/Asn1Parser.cs](../../src/Asn1Kit.Compiler/Asn1Parser.cs): разбор. Отказы — через `Error(...)`, чтобы позиция попала в `CompileException`. Если конструкция допускает соседние формы, которые ты не поддерживаешь, отклони их отдельным сообщением вместо тихого пропуска.
4. **IrBuilder.** [src/Asn1Kit.Compiler/IrBuilder.cs](../../src/Asn1Kit.Compiler/IrBuilder.cs): `ConvertType` для типов, `ResolveValue` / `ResolveDefault` для значений, `ConvertConstraint` для ограничений. Ссылки на имена резолвятся через `_typesByName` / `_valuesByName` — они уже заполнены на момент вызова, порядок объявлений в файле не важен.
5. **IR.** Если конструкция не ложится на существующий `kind` — сначала [new-ir-kind.md](new-ir-kind.md).
6. **Документация.** Профиль описан в трёх местах: [README.md](../../README.md), [docs/architecture.md](../architecture.md) («Границы профиля компилятора») и [docs/status.md](../status.md). Обнови все три.

## Тесты

В [tests/Asn1Kit.Tests/ParserTests.cs](../../tests/Asn1Kit.Tests/ParserTests.cs) и [tests/Asn1Kit.Tests/ValueResolutionTests.cs](../../tests/Asn1Kit.Tests/ValueResolutionTests.cs) тесты пишутся на встроенном фрагменте модуля — отдельный файл фикстуры заводить не нужно:

```csharp
const string asn = @"
M DEFINITIONS EXPLICIT TAGS ::= BEGIN
Flags ::= BIT STRING { a(0), b(1) }
END
";
var document = new Asn1Compiler().CompileText(asn);
```

Обязательный набор:

1. Успешный разбор: проверяются конкретные поля IR, а не «не упало».
2. Отказ на форме, оставшейся вне профиля: `Assert.Throws<CompileException>` + `Assert.Contains` по тексту.
3. Ссылки вперёд, если конструкция может ссылаться на имена (как `ub-*` в `SIZE`).
4. Валидность результата: `IrSerializer.ValidateSchema(IrSerializer.ToJson(document))`.

Если конструкция встречается в PKIX1Explicit88 — пересобери golden-фикстуру и просмотри diff:

```powershell
dotnet run --project src/Asn1Kit.Cli -- compile -i fixtures/asn1/pkix1-explicit88.asn -o fixtures/ir/pkix1-explicit88.json
dotnet test Asn1Kit.sln
```

## Типичные ловушки

- `IMPLICIT` не применяется к `CHOICE`: тег остаётся `explicit` (`IrBuilder.ResolveTag`, флаг `innerIsChoice`).
- `AUTOMATIC TAGS` нумерует компоненты по порядку — вставка компонента в середину меняет теги всех последующих.
- `DEFAULT` делает компонент `optional` и записывает разрешённое значение в `default`; для `CHOICE` и то и другое запрещено (`IrValidator`).
- Именованные числа (`INTEGER { v1(0) }`, `ENUMERATED`) ищутся и через ссылку на тип — см. `FindNamedNumber`.
