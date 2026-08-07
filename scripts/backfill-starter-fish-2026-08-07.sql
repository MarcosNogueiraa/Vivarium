-- Backfill único (07/08/2026): dá 1 peixe pronto pra coletar a TODOS os usuários
-- existentes, mesmo os que já têm peixes — mesma mecânica que agora roda automaticamente
-- no registro de conta nova (AuthEndpoints.cs). Respeita o QueueCap de cada aquário
-- (não empurra além do limite de 5 pendentes).
--
-- Rodar uma vez só contra o Neon. Idempotente na prática: rodar de novo só adiciona
-- mais 1 pra quem não estiver com a fila cheia (não duplica indevidamente, mas não
-- precisa rodar 2x).

INSERT INTO "GenerationQueueItems" ("HabitatId", "SpeciesId", "ReadyAt", "Status", "IsSick")
SELECT h."Id", sp."Id", NOW(), 'Pending', false
FROM "Habitats" h
JOIN "HabitatTypes" ht ON ht."Id" = h."HabitatTypeId" AND ht."Code" = 'Aquarium'
JOIN "Species" sp ON sp."HabitatTypeId" = ht."Id"
WHERE (
  SELECT COUNT(*) FROM "GenerationQueueItems" q
  WHERE q."HabitatId" = h."Id" AND q."Status" = 'Pending'
) < h."QueueCap";
