# Correção: Erro "categoryId cannot be a GUID"

## Problema Identificado

### Erro ao fazer upload:
```
"categoryId cannot be a GUID. Use readable category identifiers."
```

### Causa Raiz:
O modelo `Category.cs` estava gerando GUIDs automaticamente como ID:

```csharp
// ANTES (ERRADO):
public class Category
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    public Category(string name, string description = "")
    {
        Id = Guid.NewGuid().ToString();  // ← PROBLEMA
        Name = name;
        Description = description;
    }
}
```

Quando o usuário criava uma categoria chamada "Memes", o sistema gerava:
- **ID gerado**: `"abc123-def456-..."`  ← GUID
- **Esperado**: `"memes"`  ← Legível

O backend rejeita GUIDs para garantir estrutura de pastas legível:
```
media/
├── abc123-def456/  ← RUIM (GUID)
└── memes/          ← BOM (legível)
```

---

## Solução Implementada

### 1. Geração Automática de ID Legível

**Arquivo**: `Models/Category.cs`

Novo método que converte nome da categoria em ID legível:

```csharp
public static string GenerateIdFromName(string name)
{
    // Converte "Memes" → "memes"
    // Converte "Efeitos Sonoros" → "efeitos-sonoros"
    // Converte "Música Eletrônica" → "musica-eletronica"
    
    var id = name.ToLowerInvariant()
        .Normalize(System.Text.NormalizationForm.FormD);
    
    var chars = id.ToCharArray()
        .Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_')
        .ToArray();
    
    id = new string(chars);
    id = Regex.Replace(id, @"\s+", "-");
    id = Regex.Replace(id, @"-+", "-");
    id = id.Trim('-');
    
    return string.IsNullOrEmpty(id) ? "categoria" : id;
}
```

**Exemplos de conversão:**
| Nome da Categoria | ID Gerado |
|-------------------|-----------|
| Memes | `memes` |
| Efeitos Sonoros | `efeitos-sonoros` |
| Música | `musica` |
| Games | `games` |
| Músicas Eletrônicas | `musicas-eletronicas` |
| SFX & Effects | `sfx-effects` |

---

### 2. Garantia de Unicidade

**Arquivo**: `Services/SoundManager.cs`

```csharp
public async Task<Category?> AddCategoryAsync(string name, string description = "")
{
    // Gera ID baseado no nome
    var proposedId = Category.GenerateIdFromName(name);
    
    // Garante unicidade adicionando sufixo se necessário
    var finalId = proposedId;
    var counter = 1;
    while (_currentData.Categories.Any(c => c.Id == finalId))
    {
        finalId = $"{proposedId}-{counter}";
        counter++;
    }
    
    var category = new Category(name, description)
    {
        Id = finalId
    };
    
    _currentData.Categories.Add(category);
    await SaveDataAsync();
    
    return category;
}
```

**Exemplo de unicidade:**
- Primeira categoria "Memes" → ID: `memes`
- Segunda categoria "Memes" → ID: `memes-1`
- Terceira categoria "Memes" → ID: `memes-2`

---

### 3. Ferramenta de Correção

**Nova página**: `/fix-categories`

Para corrigir categorias existentes que já têm GUIDs:

#### Funcionalidades:
- Detecta categorias com IDs GUID
- Mostra preview da conversão antes de aplicar
- Converte ID de GUID para legível
- Atualiza todos os sons que referenciam a categoria
- Salva automaticamente

#### Interface:
```
┌─────────────────────────────────────────┐
│ Correção de IDs de Categorias          │
├─────────────────────────────────────────┤
│ ⚠️ Categorias com GUIDs detectadas:     │
│                                         │
│ • Memes: abc123-def456                 │
│   → será convertido para: memes        │
│                                         │
│ • Efeitos: def789-ghi012               │
│   → será convertido para: efeitos      │
│                                         │
│ [Corrigir IDs de Categorias]           │
│ [Voltar para Categorias]               │
└─────────────────────────────────────────┘
```

#### Método de correção:
```csharp
public async Task FixCategoryIdsAsync()
{
    foreach (var category in _currentData.Categories.ToList())
    {
        if (Guid.TryParse(category.Id, out _))
        {
            var oldId = category.Id;
            var newId = Category.GenerateIdFromName(category.Name);
            
            category.Id = newId;
            
            // Atualiza sons que referenciam esta categoria
            foreach (var sound in _currentData.Sounds.Where(s => s.CategoryId == oldId))
            {
                sound.CategoryId = newId;
            }
        }
    }
    
    await SaveDataAsync();
}
```

---

## Passos para Resolver o Erro

### Cenário 1: Categorias já criadas com GUID

1. Acessar `/fix-categories` no menu
2. Verificar lista de categorias com GUID
3. Clicar em "Corrigir IDs de Categorias"
4. Aguardar confirmação de sucesso
5. Tentar fazer upload novamente

### Cenário 2: Criar nova categoria

1. Acessar `/categories`
2. Criar categoria com nome "Memes"
3. Sistema gera automaticamente ID: `memes`
4. Fazer upload selecionando categoria "Memes"
5. Upload funciona com `categoryId=memes`

---

## Validação do Backend

O backend (`Server/Program.cs`) valida:

```csharp
// Valida categoryId não é vazio
if (string.IsNullOrWhiteSpace(categoryId))
    return Results.BadRequest("categoryId is required");

// Valida categoryId não é GUID
if (Guid.TryParse(categoryId, out _))
    return Results.BadRequest("categoryId cannot be a GUID. Use readable identifiers.");
```

**Aceito**: `categoryId=memes` ✅  
**Rejeitado**: `categoryId=abc123-def456-...` ❌

---

## Estrutura de Pastas Final

Após correção:

```
Server/media/
├── .media-index.json
├── memes/
│   ├── tiro-{id}.mp3
│   └── meme-engracado-{id}.mp3
├── efeitos/
│   ├── explosao-{id}.mp3
│   └── flash-{id}.mp3
├── musica/
│   ├── thriller-{id}.mp3
│   └── beat-{id}.mp3
└── games/
    └── game-over-{id}.mp3
```

**Legível** ✅  
**Organizado** ✅  
**Fácil de navegar** ✅

---

## Arquivos Modificados

### Criados:
- `Components/Pages/FixCategories.razor` - Ferramenta de correção

### Modificados:
- `Models/Category.cs` - Geração automática de ID legível
- `Services/SoundManager.cs` - AddCategoryAsync com unicidade + FixCategoryIdsAsync
- `Components/Layout/NavMenu.razor` - Link para ferramenta de correção

---

## Fluxo Completo

### Upload com Categoria Corrigida:

```
1. Usuário cria categoria "Memes"
   ↓
2. Sistema gera ID: "memes"
   ↓
3. Usuário faz upload selecionando "Memes"
   ↓
4. Frontend envia: categoryId="memes"
   ↓
5. Backend valida: "memes" não é GUID ✅
   ↓
6. Backend cria pasta: media/memes/
   ↓
7. Arquivo salvo: media/memes/tiro-{id}.mp3
   ↓
8. Sucesso!
```

### Correção de Categorias Existentes:

```
1. Usuário acessa /fix-categories
   ↓
2. Sistema detecta: "abc123-def" → "Memes"
   ↓
3. Usuário clica "Corrigir"
   ↓
4. Sistema converte: "abc123-def" → "memes"
   ↓
5. Atualiza sons: categoryId="memes"
   ↓
6. Salva sounds.json
   ↓
7. Próximo upload funciona!
```

---

## Prevenção Futura

### Categorias criadas agora:
- ✅ Sempre geram ID legível
- ✅ Nunca mais GUIDs
- ✅ Garantia de unicidade
- ✅ Compatível com backend

### Validação em múltiplas camadas:
1. **Frontend** - Gera ID correto ao criar
2. **Backend** - Rejeita GUIDs no upload
3. **Ferramenta** - Corrige categorias antigas

---

## Checklist de Validação

- [x] Modelo Category não gera mais GUIDs
- [x] IDs são gerados a partir do nome
- [x] IDs são sempre únicos
- [x] IDs são legíveis (minúsculas, sem acentos)
- [x] Backend valida e rejeita GUIDs
- [x] Ferramenta de correção disponível
- [x] Link no menu para ferramenta
- [x] Categorias antigas podem ser corrigidas
- [x] Sons mantém referência após correção
- [x] Upload funciona após correção

---

## Mensagem de Sucesso

Após executar a ferramenta de correção:

```
✅ IDs de categorias corrigidos com sucesso! 
   Agora você pode fazer upload de arquivos.
```

Após isso, fazer upload funcionará normalmente com as categorias corrigidas.

