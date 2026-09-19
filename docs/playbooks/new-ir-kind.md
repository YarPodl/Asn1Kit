# Плейбук: новый `kind` в IR

Добавление типа или значения в контракт IR. Пропуск любого шага даёт документ, который проходит схему, но падает при десериализации.

## Порядок

1. **Схема.** [schemas/asn1kit-ir-v1.json](../../schemas/asn1kit-ir-v1.json): новый вариант в `oneOf` для `typeExpr` или `value`. Обязательно `additionalProperties: false` и `required` с `kind`. Только аддитивно: существующие варианты не сужаем.
2. **Модель.** [src/Asn1Kit.Ir/IrModels.cs](../../src/Asn1Kit.Ir/IrModels.cs): класс-наследник `TypeExpr` или `IrValue` с `override string Kind`, плюс константа в `TypeKinds` / `ValueKinds`. Имя `kind` — camelCase.
3. **Сериализация.** [src/Asn1Kit.Ir/TypeExprConverter.cs](../../src/Asn1Kit.Ir/TypeExprConverter.cs): ветка в `switch` внутри `TypeExprConverter.Read` или `IrValueConverter.Read`. Забыть этот шаг — получить `IrException("Unknown type kind '...'")` на собственном же выводе.
4. **Валидация.** [src/Asn1Kit.Ir/IrValidator.cs](../../src/Asn1Kit.Ir/IrValidator.cs): семантика, которую схема не выражает (обязательные поля в контексте, запреты внутри `CHOICE`, разрешённые сочетания с `tag` / `constraint`). Текст исключения называет модуль и тип.
5. **Компилятор.** [src/Asn1Kit.Compiler/IrBuilder.cs](../../src/Asn1Kit.Compiler/IrBuilder.cs), метод `ConvertType` (для значений — `ResolveValue`): маппинг AST → новый класс. Если в AST конструкции ещё нет, сначала [compiler-construct.md](compiler-construct.md).
6. **Бэкенд.** Либо реализуешь kind (см. [csharp-backend.md](csharp-backend.md)), либо явно добавляешь его в `EnsureBackendSupport` в [src/Asn1Kit.Codegen.CSharp/CSharpBackend.cs](../../src/Asn1Kit.Codegen.CSharp/CSharpBackend.cs). Третьего варианта нет: без ветки генератор выдаст некорректный код.
7. **Фикстуры.** Если kind появляется в PKIX-модуле — пересобери golden:

   ```powershell
   dotnet run --project src/Asn1Kit.Cli -- compile -i fixtures/asn1/pkix1-explicit88.asn -o fixtures/ir/pkix1-explicit88.json
   ```

8. **Документация.** [docs/ir-schema.md](../ir-schema.md) — описание поля, [docs/status.md](../status.md) — строка в матрице.

## Тесты

- Round-trip: `IrSerializer.FromJson(IrSerializer.ToJson(document))` сохраняет все поля нового kind (`IrSchemaTests`).
- Схема отвергает документ с лишним полем и с отсутствующим обязательным.
- `IrValidator` отвергает семантически невалидное сочетание — отдельным `Assert.Throws<IrException>`.
- Если kind порождается компилятором — тест в `ParserTests` на разбор и в `PkixExplicit88Tests` на форму, если он встречается в PKIX.

```csharp
// ✅ проверяем контракт целиком, а не только «не упало»
var json = IrSerializer.ToJson(document);
IrSerializer.ValidateSchema(json);
var again = IrSerializer.FromJson(json);
Assert.IsType<RelativeOidType>(again.Modules[0].Types[0].Type);
```

## Проверка

```powershell
dotnet test Asn1Kit.sln
```
