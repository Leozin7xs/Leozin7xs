# Padrão WP — Etiquetadoras Zebra

Referência para o módulo "Problema na etiquetadora". O app só **confere** estes valores
(somente leitura). Alterar configurações do driver ainda não foi autorizado.

Fonte: prints do driver enviados pelo suporte (ZDesigner GC420t (EPL)). A TLP2844 usa o mesmo padrão.
ZD220-203dpi (ZPL): prints pendentes.

## Modelos usados nas unidades

| Modelo | Linguagem do driver | Padrão |
|---|---|---|
| ZDesigner GC420t (EPL) | EPL | este documento |
| ZDesigner TLP 2844 | EPL | este documento |
| ZDesigner ZD220-203dpi ZPL | ZPL | **pendente** |

## Aba "Options"

| Campo | Valor padrão | Observação |
|---|---|---|
| No. Of Copies | 1 | |
| Speed | **máximo do driver** | no print: 3 — confirmar se 3 é o máximo |
| Darkness | **máximo − 3** | no print: 13 — confirmar o valor exato |
| Stocks | User defined | |
| Paper Format → unidade | mm (exibição) | configurado em inch e depois trocado para mm |
| Orientação | portrait | |
| rotate 180° | **marcado** | obrigatório |
| Width × Height | **2,00 × 1,00 in** (50,8 × 25,4 mm) | |
| Unprintable Area (Left/Right/Top/Bottom) | 0,00 | |

## Aba "Advanced Setup"

| Campo | Valor padrão |
|---|---|
| Media settings | Thermal transfer |
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
