# Padrão WP — Etiquetadoras Zebra

Referência para o módulo "Problema na etiquetadora".

**Decisão do suporte:** quando uma configuração estiver fora do padrão, o app **corrige sozinho**
(volta para o valor desta tabela) e registra no log o valor antigo e o novo. Só os campos listados
aqui podem ser alterados; nenhum outro.

Fontes: prints do driver enviados pelo suporte — ZDesigner GC420t (EPL) (a TLP2844 usa o mesmo padrão)
e ZDesigner ZD230-203dpi ZPL. Os drivers EPL e ZPL têm telas e nomes de campos diferentes.

## Modelos usados nas unidades

| Modelo | Linguagem do driver | Padrão |
|---|---|---|
| ZDesigner GC420t (EPL) | EPL | este documento |
| ZDesigner TLP 2844 | EPL | este documento |
| ZDesigner ZD230-203dpi ZPL | ZPL | seção "Driver ZPL" abaixo |
| ZDesigner ZD220-203dpi ZPL | ZPL | mesmo padrão da ZD230 |

## Regras gerais (todos os modelos)

| Regra | Valor |
|---|---|
| Modo de impressão | **Térmico direto** (Direct thermal) em **todos** os modelos |
| Velocidade | **sempre a máxima** que o driver permitir |
| Escuridão | escala até 15 → **13**; escala até 30 → **24** |
| Tamanho | 50,8 × 25,4 mm (2 × 1 polegadas) |

---

# Driver EPL (GC420t, TLP2844)

## Aba "Options"

| Campo | Valor padrão | Observação |
|---|---|---|
| No. Of Copies | 1 | |
| Speed | **máximo do driver** | |
| Darkness | **13** (escala até 15) | |
| Stocks | User defined | |
| Paper Format → unidade | mm (exibição) | configurado em inch e depois trocado para mm |
| Orientação | portrait | |
| rotate 180° | **marcado** | obrigatório nos EPL (confirmado) |
| Width × Height | **2,00 × 1,00 in** (50,8 × 25,4 mm) | |
| Unprintable Area (Left/Right/Top/Bottom) | 0,00 | |

## Aba "Advanced Setup"

| Campo | Valor padrão |
|---|---|
| Media settings | **Direct thermal** (o print de referência estava em Thermal transfer, fora do padrão) |
| Media type | Label with gaps |
| Device options | None |
| Top of form backup | marcado |
| Reverse sensor | desmarcado |
| Gap/Mark Height | 3,00 mm |
| Offset | 0,00 mm |
| Cut position | 0,00 mm |
| Top offset | 1,50 mm |
| Cutter | Never |

## Aba "Dithering"

| Campo | Valor padrão | Observação |
|---|---|---|
| Dithering type | **None** | melhora o código de barras |

> **Atenção:** com Dithering = None, a **página de teste do Windows sai em branco** nesses modelos.
> Portanto o app **nunca** deve usar a página de teste do Windows para avaliar a etiquetadora.

## Calibração

Botão **Calibrate** na aba "Advanced Setup". O app deve fazer o equivalente, apenas depois de confirmar
que as configurações estão no padrão e a etiqueta continua desconfigurada. Método exato
(comando enviado à impressora) a validar em teste com cada modelo.

---

# Driver ZPL (ZD230-203dpi e ZD220-203dpi)

## Config. de página

| Campo | Valor padrão | Observação |
|---|---|---|
| Formato → Selecionar | Personalizar | |
| Largura × Altura | **50,8 × 25,4 mm** | mesmo tamanho dos modelos EPL |
| Tipo de mídia | Etiquetas com intervalos | |
| Rotação | **0° - Retrato** | diferente dos EPL, confirmado |
| Etiqueta espelho | desmarcado | |
| Inverso | desmarcado | |

## Opções de Impressão

| Campo | Valor padrão | Observação |
|---|---|---|
| Velocidade | **152 mm/s** (máxima) | |
| Escuridão | **24** (escala até 30) | |
| Modo de impressão | **Térmico direto** | |
| Offset Topo | 1 mm | |
| Offset Esquerda | 1 mm | confirmado |
| Backfeed | Padrão | |
| Pausar | Sem pausa | |
| Caracteres de controle | Padrão | |
| RTC atualizar | Horário de impressão inicial | |
| Cancelar todos os documentos antes da impressão | desmarcado | |

## Opções de gráficos

| Campo | Valor padrão | Observação |
|---|---|---|
| Interpolação | **Clipart** (extremo direito) | |
| Modo de transferência de imagem | Impressão direta | |
| Tipo de gráfico | ZIP | |

> **Atenção:** com Interpolação = Clipart, a **página de teste do Windows não sai** neste modelo, mas a
> página de teste **do próprio driver** (botão "Página de teste") sai normalmente.

O app corrige essas três opções sozinho. Mapeado em 10/11 no **ZDesigner ZD220-203dpi ZPL** (bloco interno de
13482 bytes, assinatura "NICELbl64"), em posições do DEVMODE (inteiros de 4 bytes):

| Posição | Campo | Valores |
|---|---|---|
| 1888 | Interpolação | 0 = Foto … 5 = Clipart (padrão do driver: 0) |
| 2136 | Modo de transferência | 0 = Impressão direta, 1 = Baixar (com Baixar, Tipo de gráfico fica bloqueado) |
| 1872 | Tipo de gráfico | 2 = ZIP, 11 = Padrão (com Baixar o driver grava 11) |

---

# Como o app pode ler/alterar essas configurações (investigação)

Relatório de 26/10, PC com **ZDesigner ZD230-203dpi ZPL**, driver **8.6.4.23827**, porta USB001:

- **Legível pelo Windows (formato padrão):** tamanho da etiqueta, orientação, resolução (203 dpi), cópias.
- **Não legível diretamente:** escuridão, velocidade, modo de impressão, offsets, interpolação etc.
  Ficam num bloco interno do driver (~11 KB, assinatura "Nice") dentro da configuração da impressora.
- Preferências do usuário e padrão da impressora estavam idênticos.
- **Atenção:** nesse PC a etiqueta estava configurada como **104,0 × 50,8 mm** (4 × 2 pol), não 50,8 × 25,4 mm.

Abordagem proposta ("configuração modelo"): um técnico deixa uma impressora de cada modelo exatamente no
padrão; o app guarda essa configuração completa e, nas unidades, compara e reaplica pelo próprio Windows,
somente quando o modelo **e a versão do driver** forem iguais aos do modelo guardado.

## Capturar a configuração modelo (técnico, uma vez por modelo e versão de driver)

1. Num PC com a Zebra instalada (ZD230/ZD220 ou GC420t/TLP2844), abra **Preferências de impressão**
   e deixe **todas** as abas exatamente como nas tabelas acima. Clique em **OK**.
2. Imprima pela própria tela do driver (**Página de teste**) e confira se a etiqueta sai certa.
3. Feche o WP Support Tools, se estiver aberto. No PowerShell, rode o exe com `--capturar-modelo`, por exemplo:
   `& "$env:USERPROFILE\Downloads\WPSupportTools.exe" --capturar-modelo`
4. Aparece um resumo e abre a pasta `Área de Trabalho\WPST-configuracao-modelo`. Envie os arquivos
   dessa pasta para o desenvolvimento. Eles não têm senha nem dado pessoal.

Com os arquivos, o app passa a comparar e reaplicar essa configuração nas unidades, só quando o driver
e a versão forem iguais aos do modelo.

