# Planejamento - FakeInstants (Aplicação de Áudio Offline)

## Visão Geral

Criar uma aplicação em C# .NET + Blazor que simule a funcionalidade do MyInstants, permitindo execução de áudios mediante clique. A aplicação deve ser completamente offline, portátil e executável em USB.

## Requisitos Funcionais

### ✅ Principais
- **Reprodução de áudio**: Executar sons mediante clique
- **Portabilidade**: Funcionar em USB sem instalação
- **Offline**: Não requerer internet para funcionamento básico
- **Gerenciamento de sons**: Adicionar/remover áudios dinamicamente
- **Formatos suportados**: MP3, WAV, OGG, AAC, M4A
- **Organização**: Categorização e busca de sons

### ✅ Técnicos
- **Tecnologia**: Blazor WebAssembly (cliente)
- **Persistência**: JSON para metadados + sistema de arquivos para áudios
- **Interface**: Responsiva e intuitiva
- **Performance**: Carregamento eficiente de áudios

## Arquitetura

### Tecnologias Escolhidas
- **Frontend**: Blazor WebAssembly
- **Linguagem**: C#
- **Framework**: .NET 8.0
- **Styling**: CSS/Bootstrap
- **Audio**: HTML5 Audio API + Web Audio API (fallback)

### Estrutura de Diretórios
```
fakeinstants/
├── wwwroot/
│   ├── audio/           # Pasta para áudios
│   │   ├── categories/  # Áudios organizados por categoria
│   │   └── temp/        # Upload temporário
│   ├── data/
│   │   └── sounds.json  # Metadados dos sons
│   └── assets/          # CSS, imagens, etc.
├── Components/
│   ├── AudioPlayer.razor     # Componente de reprodução
│   ├── SoundList.razor       # Lista de sons
│   ├── SoundUpload.razor     # Upload de áudios
│   └── CategoryManager.razor # Gerenciamento de categorias
└── Services/
    ├── AudioService.cs       # Lógica de áudio
    ├── SoundManager.cs       # CRUD de sons
    └── JsonStorage.cs        # Persistência JSON
```

## Estrutura de Dados

### SoundMetadata (JSON)
```json
{
  "sounds": [
    {
      "id": "string",
      "name": "string",
      "description": "string",
      "category": "string",
      "fileName": "string",
      "filePath": "string",
      "fileSize": "number",
      "duration": "number",
      "format": "string",
      "tags": ["string"],
      "createdAt": "DateTime",
      "lastPlayed": "DateTime",
      "playCount": "number",
      "favorite": "boolean"
    }
  ],
  "categories": [
    {
      "id": "string",
      "name": "string",
      "description": "string",
      "color": "string",
      "icon": "string",
      "soundCount": "number"
    }
  ]
}
```

### Configurações da Aplicação
```json
{
  "audioSettings": {
    "volume": 1.0,
    "autoplay": false,
    "repeat": false,
    "shuffle": false
  },
  "uiSettings": {
    "theme": "dark|light",
    "gridSize": "small|medium|large",
    "showDescriptions": true
  }
}
```

## Componentes Principais

### 1. SoundList
- **Responsabilidades**:
  - Exibir lista de sons organizados por categoria
  - Busca e filtros
  - Ordenação (alfabética, frequência de uso, data)
  - Visualização em grid/lista

### 2. AudioPlayer
- **Responsabilidades**:
  - Reproduzir áudio mediante clique
  - Controles de volume
  - Indicador de progresso
  - Suporte a múltiplas instâncias simultâneas
  - Cache inteligente de áudios

### 3. SoundUpload
- **Responsabilidades**:
  - Upload via drag & drop
  - Validação de formatos
  - Extração de metadados
  - Preview antes do upload
  - Organização automática por categoria

### 4. CategoryManager
- **Responsabilidades**:
  - Criar/editar/excluir categorias
  - Reorganizar sons entre categorias
  - Estatísticas por categoria
  - Cores e ícones personalizáveis

## Funcionalidades Avançadas

### Sistema de Busca
- Busca por nome, descrição ou tags
- Filtros por categoria, formato, data
- Busca fuzzy para tolerância a erros de digitação

### Favoritos
- Sistema de estrelas para sons favoritos
- Lista rápida de acesso
- Sincronização entre sessões

### Estatísticas
- Contagem de reproduções
- Tempo total de áudio
- Sons mais tocados
- Histórico de uso

### Temas e Personalização
- Tema claro/escuro
- Cores personalizáveis por categoria
- Layouts alternativos (grid, lista, cards)

## Estratégia de Portabilidade

### Publicação
- **Tipo**: Blazor WebAssembly Standalone
- **Framework**: .NET 8.0
- **Runtime**: Incluído na publicação
- **Servidor**: Kestrel embutido (opcional)

### Estrutura para USB
```
USB_ROOT/
├── FakeInstants.exe     # Executável principal
├── wwwroot/            # Assets estáticos
├── audio/              # Biblioteca de sons
├── data/               # Arquivos JSON
└── _framework/         # Runtime .NET
```

### Considerações
- **Tamanho**: Otimizar assets (compressão de áudio, minificação)
- **Compatibilidade**: Funcionar em Windows 7+ (suporte a .NET 8)
- **Permissões**: Acesso a sistema de arquivos para leitura/escrita
- **Backup**: Sistema de backup automático dos dados

## Etapas de Desenvolvimento

### Fase 1: Setup e Estrutura Básica
1. Criar projeto Blazor WebAssembly
2. Configurar estrutura de diretórios
3. Implementar serviços básicos (JSON, FileSystem)
4. Criar layout base da aplicação

### Fase 2: Sistema de Áudio
1. Implementar AudioService
2. Suporte a múltiplos formatos
3. Cache de áudios
4. Controles de reprodução

### Fase 3: Gerenciamento de Sons
1. Componente de upload
2. Validação e processamento de arquivos
3. Estrutura de metadados
4. Sistema de categorias

### Fase 4: Interface do Usuário
1. SoundList component
2. AudioPlayer component
3. Funcionalidades de busca e filtro
4. Tema e responsividade

### Fase 5: Recursos Avançados
1. Sistema de favoritos
2. Estatísticas e analytics
3. Configurações personalizáveis
4. Otimizações de performance

### Fase 6: Portabilidade e Distribuição
1. Configurar publicação standalone
2. Otimizações de tamanho
3. Testes de compatibilidade
4. Criar instalador/script de distribuição

## Considerações Técnicas

### Audio API
- **HTML5 Audio**: Para formatos suportados nativamente
- **Web Audio API**: Para processamento avançado (fallback)
- **Compatibilidade**: Detectar suporte e usar fallback apropriado

### File System Access
- **Blazor WASM**: Limitações no acesso ao sistema de arquivos
- **Solução**: Usar File System Access API (modern browsers)
- **Fallback**: Input file tradicional

### Performance
- **Lazy Loading**: Carregar áudios sob demanda
- **Cache**: Implementar cache inteligente
- **Compressão**: Otimizar tamanho dos arquivos JSON

### Segurança
- **Validação**: Verificar tipos de arquivo no upload
- **Sanitização**: Limpar metadados de arquivos
- **Isolamento**: Executar em sandbox

## Riscos e Mitigações

### Risco: Limitações do Blazor WASM
- **Mitigação**: Usar PWA + Service Workers para cache offline

### Risco: Suporte limitado a formatos de áudio
- **Mitigação**: Usar bibliotecas JavaScript para codecs adicionais

### Risco: Performance com muitos arquivos
- **Mitigação**: Implementar paginação e lazy loading

### Risco: Acesso ao sistema de arquivos
- **Mitigação**: Usar File System Access API com fallbacks

## Métricas de Sucesso

- ✅ Aplicação executa sem internet
- ✅ Funciona em USB em diferentes computadores
- ✅ Suporte aos principais formatos de áudio
- ✅ Interface intuitiva e responsiva
- ✅ Performance aceitável com 100+ sons
- ✅ Backup automático de configurações

## Próximos Passos

1. **Revisão do Planejamento**: Aprovação dos requisitos e arquitetura
2. **Setup Inicial**: Criar projeto base e estrutura
3. **Prototipagem**: Implementar funcionalidades core
4. **Iteração**: Desenvolvimento incremental com testes
5. **Otimização**: Performance e portabilidade
6. **Distribuição**: Empacotamento final

---

**Status**: Planejamento Concluído ✅
**Aguardando**: Liberação para implementação

