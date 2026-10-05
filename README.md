# WP Support Tools

Ferramenta Windows de primeiro atendimento para as unidades do Grupo WP.
O funcionário abre o aplicativo, clica em **Corrigir problema de impressão**, e o aplicativo
executa uma sequência fixa de procedimentos de manutenção, mostrando o progresso passo a passo.

> **Status atual (v0.1.0): arquitetura e interface.**
> Os procedimentos de impressão estão em **modo de simulação**: nenhum comando real é executado
> no Windows até que os comandos oficiais da WP sejam fornecidos e implementados.

---

## Tecnologia

| Item | Escolha |
|---|---|
| Linguagem / plataforma | C# com **.NET 10 (LTS)** |
| Interface | **WPF** (nativa do Windows) |
| Ações no Windows | APIs do .NET/Windows (ex.: `ServiceController`, `System.IO`) e, quando necessário, PowerShell com script fixo embutido |
| Elevação | UAC (mecanismo oficial do Windows), sob demanda |
| Distribuição | Um único `WPSupportTools.exe`, sem dependências |

## Estrutura

```
WPSupportTools.sln
├── src/
│   ├── WPSupportTools.Core/            Lógica (sem interface)
│   │   ├── Procedures/                 Contrato de procedimento, sequência
│   │   ├── Execution/                  Executor determinístico, eventos, protocolo do modo elevado
│   │   ├── Logging/                    Log local em arquivo
│   │   └── Modules/
│   │       ├── ModuleCatalog.cs        Módulos exibidos na tela inicial
│   │       ├── SequenceCatalog.cs      Lista FECHADA de sequências executáveis
│   │       └── Printing/               Impressão
│   │           ├── Correction/
│   │           │   ├── PrintingFixSequence.cs    ← ordem das etapas
│   │           │   └── Procedures/               ← um arquivo por procedimento
│   │           └── Diagnostics/                  (futuro)
│   └── WPSupportTools.App/             Interface WPF + modo elevado
│       ├── Elevation/                  UAC, canal local com o processo administrador
│       ├── ViewModels/
│       └── MainWindow.xaml
└── tests/WPSupportTools.Core.Tests/    Testes automatizados
```

Módulos futuros (UniLab, Rede, Telefonia) entram como novas pastas em `Modules/`, registradas em
`ModuleCatalog` e `SequenceCatalog`. Nada mais precisa mudar.

## Sequência de impressão (atual)

Definida em `src/WPSupportTools.Core/Modules/Printing/Correction/PrintingFixSequence.cs`:

| # | Procedimento | ID (log) | Admin | Status |
|---|---|---|---|---|
| 1 | Verificando serviço de impressão | `printing.check-spooler` | não | aguardando comando |
| 2 | Parando serviço de impressão | `printing.stop-spooler` | sim | aguardando comando |
| 3 | Limpando fila de impressão | `printing.clear-queue` | sim | aguardando comando |
| 4 | Iniciando serviço de impressão | `printing.start-spooler` | sim | aguardando comando |
| 5 | Reiniciando componentes necessários | `printing.restart-components` | sim | aguardando comando |
| 6 | Finalizando manutenção | `printing.finalize` | não | aguardando comando |

Para mudar a ordem ou remover uma etapa, altere apenas a lista em `PrintingFixSequence.Create()`.

### Regras de execução

- As etapas rodam **na ordem**, uma de cada vez.
- **Na primeira falha a sequência para.** As etapas seguintes são marcadas como "não executadas".
- Não há novas tentativas nem ações alternativas automáticas.
- Cada etapa tem tempo limite (padrão: 2 min); estourar o tempo conta como falha.
- Falta de permissão é informada como "permissão de administrador necessária".
- A janela não pode ser fechada no meio da manutenção (para não deixar o spooler parado).

## Elevação de privilégios (UAC)

1. A interface abre **sem** privilégios de administrador (`asInvoker` em `app.manifest`).
2. Ao iniciar uma sequência que contém etapas administrativas, o app inicia **o próprio executável**
   com o verbo `runas`. O Windows mostra o aviso do UAC.
3. O processo elevado recebe somente `--worker --sequence printing.fix --pipe <nome> --parent <pid>`.
   Ele só aceita IDs presentes no `SequenceCatalog`: **não existe forma de passar um comando**.
4. A comunicação é por *named pipe* local com nome aleatório. Os dois lados conferem o PID um do outro
   (`GetNamedPipeClientProcessId` / `GetNamedPipeServerProcessId`).
5. Se o usuário recusar o UAC, nada é executado e a tela informa que a permissão é necessária.

Recomendações de implantação:
- Instalar em `C:\Program Files\WP Support Tools\` (pasta que usuários comuns não podem alterar).
- **Assinar o executável** com o certificado de assinatura de código da WP, para o UAC mostrar
  "Editor verificado: Grupo WP" e o SmartScreen não bloquear.

## Log

- Local: `%LOCALAPPDATA%\WPSupportTools\Logs\wpst-AAAAMMDD.log` (um arquivo por dia).
- Registra: data/hora, versão, cada etapa (ID, nome, resultado), mensagem de erro, retorno do usuário.
- Não registra nome de usuário, nome do computador nem dados pessoais.
- Botão "Abrir pasta de logs" no rodapé.

Exemplo:

```
[2026-10-05 22:41:03] [INFO ] Início: printing.fix — Corrigindo problema de impressão… (WP Support Tools v0.1.0; processo elevado sob demanda).
[2026-10-05 22:41:04] [INFO ] Etapa 1/6 [printing.check-spooler] Verificando serviço de impressão: iniciando
[2026-10-05 22:41:04] [INFO ] Etapa 1/6 [printing.check-spooler] Verificando serviço de impressão: OK — Serviço Spooler encontrado
[2026-10-05 22:41:05] [ERRO ] Etapa 2/6 [printing.stop-spooler] Parando serviço de impressão: FALHA — ... | Erro: ...
[2026-10-05 22:41:05] [ERRO ] Fim: printing.fix — resultado: falhou (etapa com falha: printing.stop-spooler)
```

## Internet

O aplicativo **não faz nenhuma conexão de rede**: sem telemetria, sem verificação de atualização,
sem envio de logs. Tudo roda localmente.

## Compilar, testar e gerar o .exe

Pré-requisito (somente na máquina de desenvolvimento): [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
dotnet test                                  # testes automatizados
dotnet run --project src\WPSupportTools.App  # abrir o app (Windows)
powershell -File build\publish.ps1           # gera publish\win-x64\WPSupportTools.exe
```

O `.exe` gerado é *self-contained* (≈60 MB) e roda em Windows 10/11 x64 sem instalar o .NET.

## Como um procedimento real será implementado

Hoje cada etapa herda de `NotConfiguredProcedure` (simulação). Quando o comando oficial for definido,
a classe passa a implementar `IProcedure` diretamente:

```csharp
public sealed class StopPrintSpoolerProcedure : IProcedure
{
    public string Id => "printing.stop-spooler";
    public string DisplayName => "Parando serviço de impressão";
    public bool RequiresElevation => true;

    public async Task<ProcedureResult> ExecuteAsync(ProcedureContext context, CancellationToken ct)
    {
        // 1. Validar que o alvo existe e é o esperado (se não for: retornar falha, sem alternativas).
        // 2. Executar somente o comando autorizado.
        // 3. Confirmar o resultado e retornar Ok/Fail com mensagem clara.
    }
}
```

O que o programa **nunca** faz: instalar/remover programas, alterar drivers, rede ou registro sem
autorização explícita, apagar arquivos fora das pastas autorizadas, executar comandos arbitrários,
tentar soluções extras automaticamente, acessar outros computadores ou enviar dados para fora.
