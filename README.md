# FakeInstants 🎵

Aplicação offline para reprodução de sons, inspirada no MyInstants. Desenvolvida em C# .NET + Blazor WebAssembly para ser completamente portátil e executável em USB.

## 🚀 Funcionalidades

- **Reprodução de áudio**: Clique para tocar sons instantaneamente
- **Completamente offline**: Não requer internet após carregamento inicial
- **Portátil**: Funciona em USB, pode ser levado para qualquer computador
- **Suporte a múltiplos formatos**: MP3, WAV, OGG, AAC, M4A, FLAC
- **Organização por categorias**: Mantenha seus sons organizados
- **Sistema de favoritos**: Marque seus sons preferidos
- **Upload fácil**: Arraste e solte arquivos ou use o seletor
- **Busca inteligente**: Encontre sons rapidamente
- **Interface moderna**: Design responsivo e intuitivo

## 📁 Estrutura do Projeto

```
FakeInstants/
├── wwwroot/
│   ├── audio/
│   │   ├── categories/    # Sons organizados por categoria
│   │   └── temp/         # Arquivos temporários de upload
│   ├── data/
│   │   ├── sounds.json   # Metadados dos sons
│   │   └── settings.json # Configurações da aplicação
│   └── assets/           # Recursos estáticos
├── _framework/           # Runtime .NET (gerado na publicação)
└── index.html           # Arquivo principal da aplicação
```

## 🛠️ Como Usar

### Opção 1: Executar via Visual Studio
1. Abra o arquivo `fakeinstants.sln`
2. Execute o projeto (F5)

### Opção 2: Executar via linha de comando
```bash
dotnet run
```

### Opção 3: Versão Portátil (USB)
1. Execute o arquivo `publish.bat` ou copie a pasta `bin/Release/net8.0/publish` para seu USB
2. Execute o arquivo `fakeinstants.exe` na pasta `publish`

## 📤 Distribuição

Para criar uma versão portátil:

```bash
# Publicar como standalone
dotnet publish -c Release -p:PublishTrimmed=false --self-contained true -r win-x64

# A versão publicada estará em: bin/Release/net8.0/publish/
```

## 🎯 Categorias Padrão

- **Memes**: Sons de memes e virais
- **Efeitos**: Efeitos sonoros diversos
- **Música**: Trechos musicais
- **Games**: Sons de jogos
- **Outros**: Outros tipos de sons

## ⚙️ Configurações

A aplicação salva automaticamente:
- Lista de sons e metadados
- Preferências do usuário (tema, volume, etc.)
- Estatísticas de uso

## 🔧 Desenvolvimento

### Pré-requisitos
- .NET 8.0 SDK
- Navegador moderno com suporte a WebAssembly

### Build e Execução
```bash
# Restaurar dependências
dotnet restore

# Build
dotnet build

# Executar
dotnet run

# Publicar
dotnet publish -c Release -p:PublishTrimmed=false --self-contained true -r win-x64
```

## 📝 Arquitetura

- **Frontend**: Blazor WebAssembly
- **Backend**: .NET 8.0
- **Persistência**: JSON + Sistema de Arquivos
- **Áudio**: HTML5 Audio API + Web Audio API
- **UI**: Bootstrap + CSS Customizado

## 🐛 Problemas Conhecidos

- Drag & drop de arquivos ainda em desenvolvimento
- Alguns formatos de áudio podem não ser suportados em navegadores antigos

## 📄 Licença

Este projeto é open source e está disponível sob a licença MIT.

## 🤝 Contribuição

Contribuições são bem-vindas! Sinta-se à vontade para:
- Reportar bugs
- Sugerir novas funcionalidades
- Enviar pull requests

---

**Desenvolvido com ❤️ para amantes de sons e memes!**

