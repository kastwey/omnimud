-- Balzhur: reglas de mensajes (port de ProcessRules\CBalzhur.cs del cliente original).
--
-- Se miran todas las lineas no vacias del bloque y las que son mensajes se ACUMULAN en un unico
-- mensaje, una por linea:
--  * canales:  "[Canal: Nombre]: '...",  "[Canal]: ..."  y  "[Canal] ..."  para los canales de
--    la lista (con al menos una palabra mas en la linea);
--  * lo que dices tu: lineas que empiezan por "Charlas '", "Cuentas a ", "Dices '",
--    "Respondes a " o "Susurras a ";
--  * lo que te dicen: "X charla '...", "X te responde '...", "X te cuenta '...", "X te susurra '...".
-- Las palabras se separan por espacios y los espacios repetidos o iniciales no cuentan.
--
-- Contrato: el script lee om.lines (tabla, empieza en 1) y llama a om.message(texto).

local CHANNELS = { "Clan", "Faccion", "Novatos", "Orden", "Concilio", "Gremio", "Raza", "Avatar", "Reino", "Trivial", "GOSOCIAL" }

-- Primera palabra de una linea de canal, en sus dos formas.
local withName, plain = {}, {}
for _, channel in ipairs(CHANNELS) do
  withName["[" .. channel .. ":"] = true      -- [Canal: Nombre]: '...
  plain["[" .. channel .. "]:"] = true        -- [Canal]: ...
  plain["[" .. channel .. "]"] = true         -- [Canal] ...
end

local OWN = [[^(?:Charlas '|Cuentas a |Dices '|Respondes a |Susurras a )]]
-- Las cuatro primeras palabras (String.Split con RemoveEmptyEntries del original).
local WORDS = [[^ *([^ ]+)(?: +([^ ]+))?(?: +([^ ]+))?(?: +([^ ]+))?]]

local function q(word)
  return word ~= nil and string.sub(word, 1, 1) == "'"
end

local function isMessage(line)
  local w = om.match(line, WORDS)
  local w1, w2, w3, w4
  if w then w1, w2, w3, w4 = w[1], w[2], w[3], w[4] end

  if w2 ~= nil and string.sub(w1, 1, 1) == "[" then
    if plain[w1] then return true end
    if withName[w1] and string.sub(w2, -2) == "]:" and q(w3) then return true end
  end

  if om.match(line, OWN) then return true end

  return (w2 == "charla" and q(w3))
      or (w2 == "te" and (w3 == "responde" or w3 == "cuenta" or w3 == "susurra") and q(w4))
end

local found = {}
for _, line in ipairs(om.lines) do
  if line ~= "" and isMessage(line) then
    found[#found + 1] = line
  end
end

if #found > 0 then
  om.message(table.concat(found, "\n"))
end
