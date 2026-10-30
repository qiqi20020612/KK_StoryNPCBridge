# Runtime ID reference

English | [简体中文](REFERENCE.zh-CN.md)

Reference mappings for the original Japanese release of Koikatu and the default female model (`ill_Default_Female`). Use the live `catalog` for the available IDs, model-specific patterns and current game conditions. Native morph identifiers are source data; English expression labels interpret those identifiers.

[API command and reference input rules](../API.md). Callable references accept a returned object, its `key`, or its numeric `id` in the matching input domain. Game-time references are read-only. Queue tasks use the same action and map IDs; task IDs themselves are unique run identifiers.

## Relationships

Use `set_stats.changes.relation`. A relationship reference updates meeting, friendship and girlfriend flags together. The native getter derives the result from those flags. Attribute types, ranges and all writable flags are documented in [Native attributes and relationships](../API.md#native-attributes-and-relationships).

| ID | Key | English meaning |
| --- | --- | --- |
| -1 | `stranger` | Not met |
| 0 | `acquaintance` | Acquaintance |
| 1 | `friend` | Friend |
| 2 | `girlfriend` | Girlfriend |

## Game time

These IDs describe observations from `gameTime`; they are not scheduler action IDs or clock-write commands. The game exposes weekdays, not an absolute calendar date. See [Player and native game time](../API.md#player-and-native-game-time) for day counters and events.

### Native stages

| ID | Key | English meaning |
| --- | --- | --- |
| 0 | `wake_up` | Wake up |
| 1 | `morning` | Morning |
| 2 | `going_to_school` | Going to school |
| 3 | `homeroom_1` | Morning homeroom |
| 4 | `lesson_1` | Morning classes |
| 5 | `lunch_time` | Lunch |
| 6 | `lesson_2` | Afternoon classes |
| 7 | `homeroom_2` | Afternoon homeroom |
| 8 | `staff_time` | Staff period |
| 9 | `after_school` | After school |
| 10 | `going_home` | Going home |
| 11 | `my_house` | At home / night |

### UI periods

Stages 0–4 map to period 0, 5–7 to 1, 8 to 2, 9–10 to 3 and 11 to 4.

| ID | Key | English meaning |
| --- | --- | --- |
| 0 | `morning` | Morning |
| 1 | `midday` | Lunch / afternoon classes |
| 2 | `staff_time` | Staff period |
| 3 | `after_school` | After school |
| 4 | `night` | Night |

### Weekdays

| ID | Key | English meaning |
| --- | --- | --- |
| 0 | `monday` | Monday |
| 1 | `tuesday` | Tuesday |
| 2 | `wednesday` | Wednesday |
| 3 | `thursday` | Thursday |
| 4 | `friday` | Friday |
| 5 | `saturday` | Saturday |
| 6 | `holiday` | Sunday / holiday |

## Native scheduler actions

Use in `set_action.action`, `queue_action.action` or `set_desire.action`. Action -1 is plugin idle and is not schedulable; use `stop`. A valid action ID can still be unavailable because of game conditions, occupied points or missing native targets. The `mapsWithPoints` array in the live result identifies maps with the correct point layer and `mapDetails` explains each one.

| ID | Key | English meaning | Native point layer |
| --- | --- | --- | --- |
| 0 | `change_clothes` | Change clothes | Dress |
| 1 | `use_toilet` | Use toilet | Toilet |
| 2 | `shower` | Take a shower | Shower |
| 3 | `eat` | Eat | Dinner |
| 4 | `solo_intimacy` | Solo intimate activity | Masturbation |
| 5 | `seek_player_intimacy` | Seek intimacy with player | WishH |
| 6 | `club_activity` | Club activity | Club |
| 7 | `talk_to_npc` | Talk to another NPC | Talk |
| 8 | `talk_to_player` | Talk to player | Talk |
| 9 | `read` | Read | Reading |
| 10 | `use_phone` | Use phone | Phone |
| 11 | `play_phone_game` | Play phone game | Game |
| 12 | `take_selfie` | Take a selfie | Shooting |
| 13 | `listen_to_music` | Listen to music | Music |
| 14 | `practice_dance` | Practice dancing | Dance |
| 15 | `groom` | Groom appearance | Appearance |
| 16 | `drink` | Have a drink | Drink |
| 17 | `take_break` | Take a break | ChangeMind |
| 18 | `exercise` | Exercise | Sport |
| 19 | `personality_activity` | Personality-specific activity | Personality |
| 20 | `flee` | Flee | Wait |
| 21 | `study` | Study | Study |
| 22 | `sleep` | Sleep | Sleep |
| 23 | `follow_player` | Follow player | Talk |
| 24 | `respond_to_call` | Respond to player call | Talk |
| 25 | `shy_response` | Shy response | Talk |
| 26 | `paired_intimacy` | Paired intimate activity | Lesbian |
| 27 | `paired_intimacy_partner` | Paired intimate activity partner | Lesbian |
| 28 | `await_talk_response` | Wait for talk response | ResponseTalk |
| 29 | `await_intimacy_response` | Wait for intimacy response | ResponseH |
| 30 | `hesitate` | Hesitant idle | HesitantlyWait |

## Maps

Use a map reference in task `map`. `automatic` (-1) asks the scheduler to choose. Map names identify locations, not current availability. Check `canSchedule` in the live result before choosing a task destination; event-only maps and private rooms may be unavailable.

| ID | Key | English meaning |
| --- | --- | --- |
| -1 | `automatic` | Automatic map selection |
| 0 | `map/0` | Player room |
| 2 | `map/2` | School first floor |
| 3 | `map/3` | School second floor |
| 4 | `map/4` | School third floor |
| 5 | `map/5` | Classroom 1-1 |
| 8 | `map/8` | Classroom 2-1 |
| 9 | `map/9` | Classroom 2-2 |
| 11 | `map/11` | Classroom 3-1 |
| 14 | `map/14` | Girls restroom, first floor |
| 15 | `map/15` | Girls restroom, second floor |
| 16 | `map/16` | Girls restroom, third floor |
| 17 | `map/17` | Staff room |
| 18 | `map/18` | Male staff restroom |
| 20 | `map/20` | Infirmary |
| 21 | `map/21` | Library |
| 22 | `map/22` | Club room |
| 26 | `map/26` | Manga club room |
| 28 | `map/28` | Japanese-style room |
| 31 | `map/31` | Gym storage |
| 32 | `map/32` | Gymnasium |
| 33 | `map/33` | Courtyard |
| 34 | `map/34` | Athletic field |
| 36 | `map/36` | Rooftop |
| 37 | `map/37` | Swimming pool |
| 38 | `map/38` | Cafeteria |
| 45 | `map/45` | Shower room |
| 46 | `map/46` | Locker room |
| 47 | `map/47` | Backyard |
| 50 | `map/50` | Gymnasium live performance |
| 51 | `map/51` | Restroom event view |
| 52 | `map/52` | Shower event view |
| 53 | `map/53` | Locker room event view |
| 60 | `map/60` | Love hotel |
| 61 | `map/61` | Karaoke |
| 62 | `map/62` | Church |
| 63 | `map/63` | Crowded train |
| 64 | `map/64` | Train |
| 65 | `map/65` | Park |
| 66 | `map/66` | Church event |
| 70 | `map/70` | Equipment storage |

## Expression patterns

Indices are local to the actor model and facial field. A mouth reference cannot be applied to eyes. Use `set_expression.changes.eyes`, `.eyebrow` or `.mouth`. The first mesh's exact closed/open morph targets are shown; the live entry includes all mesh targets. Visual strength also depends on `eyesOpen`, `mouthOpen` and blinking. Unknown custom morph combinations are labeled explicitly rather than assigned a guessed default-model expression.


### eyes

| ID | Key | English meaning | Closed target | Open target |
| --- | --- | --- | --- | --- |
| 0 | `eyes/0` | Neutral | `eye_face.f00_def_cl` | `eye_face.f00_def_op` |
| 1 | `eyes/1` | Closed neutral eyes | `eye_face.f00_def_cl` | `eye_face.f00_def_cl` |
| 2 | `eyes/2` | Smiling eyes | `eye_face.f00_egao_cl` | `eye_face.f00_egao_op` |
| 3 | `eyes/3` | Closed smiling eyes | `eye_face.f00_egao_cl` | `eye_face.f00_egao_cl` |
| 4 | `eyes/4` | Soft smile | `eye_face.f00_egao_cl` | `eye_face.f00_bisyou_op` |
| 5 | `eyes/5` | Left wink | `eye_face.f00_winkl_op` | `eye_face.f00_winkl_op` |
| 6 | `eyes/6` | Right wink | `eye_face.f00_winkr_op` | `eye_face.f00_winkr_op` |
| 7 | `eyes/7` | Wistful | `eye_face.f00_def_cl` | `eye_face.f00_setunai_op` |
| 8 | `eyes/8` | Embarrassed | `eye_face.f00_def_cl` | `eye_face.f00_tere_op` |
| 9 | `eyes/9` | Angry | `eye_face.f00_def_cl` | `eye_face.f00_ikari_op` |
| 10 | `eyes/10` | Serious | `eye_face.f00_def_cl` | `eye_face.f00_sinken_op` |
| 11 | `eyes/11` | Bored | `eye_face.f00_def_cl` | `eye_face.f00_tumara_op` |
| 12 | `eyes/12` | Pained | `eye_face.f00_def_cl` | `eye_face.f00_kurusi_op` |
| 13 | `eyes/13` | Disgusted | `eye_face.f00_def_cl` | `eye_face.f00_keno_op` |
| 14 | `eyes/14` | Thoughtful | `eye_face.f00_def_cl` | `eye_face.f00_sian_op` |
| 15 | `eyes/15` | Sad | `eye_face.f00_def_cl` | `eye_face.f00_kanasi_op` |
| 16 | `eyes/16` | Crying | `eye_face.f00_def_cl` | `eye_face.f00_naki_op` |
| 17 | `eyes/17` | Anxious | `eye_face.f00_def_cl` | `eye_face.f00_aseri_op` |
| 18 | `eyes/18` | Dejected | `eye_face.f00_def_cl` | `eye_face.f00_rakutan_op` |
| 19 | `eyes/19` | Troubled | `eye_face.f00_def_cl` | `eye_face.f00_komaru_op` |
| 20 | `eyes/20` | Smug | `eye_face.f00_def_cl` | `eye_face.f00_doya_op` |
| 21 | `eyes/21` | Comical eyes | `eye_face.f00_gag` | `eye_face.f00_gag` |
| 22 | `eyes/22` | Left eye squeeze | `eye_face.f00_gyul_op` | `eye_face.f00_gyul_op` |
| 23 | `eyes/23` | Right eye squeeze | `eye_face.f00_gyur_op` | `eye_face.f00_gyur_op` |
| 24 | `eyes/24` | Both eyes squeezed shut | `eye_face.f00_gyu_cl` | `eye_face.f00_gyu_cl` |
| 25 | `eyes/25` | Left eye squeeze, variant 2 | `eye_face.f00_gyul02_op` | `eye_face.f00_gyul02_op` |
| 26 | `eyes/26` | Right eye squeeze, variant 2 | `eye_face.f00_gyur02_op` | `eye_face.f00_gyur02_op` |
| 27 | `eyes/27` | Both eyes squeezed shut, variant 2 | `eye_face.f00_gyu02_cl` | `eye_face.f00_gyu02_cl` |

### eyebrow

| ID | Key | English meaning | Closed target | Open target |
| --- | --- | --- | --- | --- |
| 0 | `eyebrow/0` | Neutral eyebrows | `mayuge.mayu00_def_cl` | `mayuge.mayu00_def_op` |
| 1 | `eyebrow/1` | Angry eyebrows | `mayuge.mayu00_oko_cl` | `mayuge.mayu00_oko_op` |
| 2 | `eyebrow/2` | Troubled eyebrows | `mayuge.mayu00_koma_cl` | `mayuge.mayu00_koma_op` |
| 3 | `eyebrow/3` | Bored eyebrows | `mayuge.mayu00_tmara_cl` | `mayuge.mayu00_tmara_op` |
| 4 | `eyebrow/4` | Questioning, left | `mayuge.mayu00_gimoL_cl` | `mayuge.mayu00_gimoL_op` |
| 5 | `eyebrow/5` | Questioning, right | `mayuge.mayu00_gimoR_cl` | `mayuge.mayu00_gimoR_op` |
| 6 | `eyebrow/6` | Thinking, left | `mayuge.mayu00_sianL_cl` | `mayuge.mayu00_sianL_op` |
| 7 | `eyebrow/7` | Thinking, right | `mayuge.mayu00_sianR_cl` | `mayuge.mayu00_sianR_op` |
| 8 | `eyebrow/8` | Angry, left | `mayuge.mayu00_oko2L_cl` | `mayuge.mayu00_oko2L_op` |
| 9 | `eyebrow/9` | Angry, right | `mayuge.mayu00_oko2R_cl` | `mayuge.mayu00_oko2R_op` |
| 10 | `eyebrow/10` | Serious eyebrows | `mayuge.mayu00_sinken_cl` | `mayuge.mayu00_sinken_op` |
| 11 | `eyebrow/11` | Worried eyebrows | `mayuge.mayu00_huan_cl` | `mayuge.mayu00_huan_op` |
| 12 | `eyebrow/12` | Surprised eyebrows | `mayuge.mayu00_odoro_cl` | `mayuge.mayu00_odoro_op` |
| 13 | `eyebrow/13` | Dejected eyebrows | `mayuge.mayu00_rakutan_cl` | `mayuge.mayu00_rakutan_op` |
| 14 | `eyebrow/14` | Smug eyebrows | `mayuge.mayu00_doya_cl` | `mayuge.mayu00_doya_op` |
| 15 | `eyebrow/15` | Left-wink eyebrows | `mayuge.mayu00_winkl_cl` | `mayuge.mayu00_winkl_op` |
| 16 | `eyebrow/16` | Right-wink eyebrows | `mayuge.mayu00_winkr_cl` | `mayuge.mayu00_winkr_op` |

### mouth

| ID | Key | English meaning | Closed target | Open target |
| --- | --- | --- | --- | --- |
| 0 | `mouth/0` | Neutral mouth | `kuti_face.f00_def_cl` | `kuti_face.f00_def_op` |
| 1 | `mouth/1` | Smile | `kuti_face.f00_bisyou_op` | `kuti_face.f00_egao_op` |
| 2 | `mouth/2` | Joyful smile | `kuti_face.f00_uresi_cl` | `kuti_face.f00_uresi_op` |
| 3 | `mouth/3` | Small joyful smile | `kuti_face.f00_bisyou_op` | `kuti_face.f00_uresi_s_op` |
| 4 | `mouth/4` | Tiny joyful smile | `kuti_face.f00_bisyou_op` | `kuti_face.f00_uresi_ss_op` |
| 5 | `mouth/5` | Flustered mouth | `kuti_face.f00_doki_cl` | `kuti_face.f00_doki_s_op` |
| 6 | `mouth/6` | Small flustered mouth | `kuti_face.f00_doki_s_cl` | `kuti_face.f00_doki_s_op` |
| 7 | `mouth/7` | Tiny flustered mouth | `kuti_face.f00_doki_ss_cl` | `kuti_face.f00_doki_ss_op` |
| 8 | `mouth/8` | Angry mouth | `kuti_face.f00_ikari_cl` | `kuti_face.f00_ikari_op` |
| 9 | `mouth/9` | Angry mouth, variant 2 | `kuti_face.f00_ikari_cl` | `kuti_face.f00_ikari02_op` |
| 10 | `mouth/10` | Serious narrow mouth | `kuti_face.f00_doki_ss_cl` | `kuti_face.f00_sinken02_op` |
| 11 | `mouth/11` | Serious pursed mouth | `kuti_face.f00_sinken03_cl` | `kuti_face.f00_sinken03_op` |
| 12 | `mouth/12` | Disgusted mouth | `kuti_face.f00_sinken03_cl` | `kuti_face.f00_keno_op` |
| 13 | `mouth/13` | Lonely mouth | `kuti_face.f00_doki_ss_cl` | `kuti_face.f00_sabisi_op` |
| 14 | `mouth/14` | Anxious mouth | `kuti_face.f00_sinken03_cl` | `kuti_face.f00_aseri_op` |
| 15 | `mouth/15` | Dissatisfied open mouth | `kuti_face.f00_human_cl` | `kuti_face.f00_o_l_op` |
| 16 | `mouth/16` | Exasperated mouth | `kuti_face.f00_doki_ss_cl` | `kuti_face.f00_akire_op` |
| 17 | `mouth/17` | Surprised open mouth | `kuti_face.f00_def_cl` | `kuti_face.f00_odoro_op` |
| 18 | `mouth/18` | Small surprised mouth | `kuti_face.f00_def_cl` | `kuti_face.f00_odoro_s_op` |
| 19 | `mouth/19` | Smug smile | `kuti_face.f00_doya_cl` | `kuti_face.f00_doya_op` |
| 20 | `mouth/20` | Tongue out | `kuti_face.f00_pero_cl` | `kuti_face.f00_pero_cl` |
| 21 | `mouth/21` | Licking mouth shape | `kuti_face.f00_def_cl` | `kuti_face.f00_name_op` |
| 22 | `mouth/22` | Rounded holding mouth shape | `kuti_face.f00_def_cl` | `kuti_face.f00_kuwae_op` |
| 23 | `mouth/23` | Puckered lips | `kuti_face.f00_kisu_op` | `kuti_face.f00_kisu_op` |
| 24 | `mouth/24` | Licking mouth shape, variant 2 | `kuti_face.f00_name02_op` | `kuti_face.f00_name02_op` |
| 25 | `mouth/25` | Vowel A, small | `kuti_face.f00_n_s_cl` | `kuti_face.f00_a_s_op` |
| 26 | `mouth/26` | Vowel A, large | `kuti_face.f00_n_l_cl` | `kuti_face.f00_a_l_op` |
| 27 | `mouth/27` | Vowel I, small | `kuti_face.f00_n_s_cl` | `kuti_face.f00_i_s_op` |
| 28 | `mouth/28` | Vowel I, large | `kuti_face.f00_n_l_cl` | `kuti_face.f00_i_l_op` |
| 29 | `mouth/29` | Vowel U, small | `kuti_face.f00_n_s_cl` | `kuti_face.f00_u_s_op` |
| 30 | `mouth/30` | Vowel U, large | `kuti_face.f00_n_l_cl` | `kuti_face.f00_u_l_op` |
| 31 | `mouth/31` | Vowel E, small | `kuti_face.f00_n_s_cl` | `kuti_face.f00_e_s_op` |
| 32 | `mouth/32` | Vowel E, large | `kuti_face.f00_n_l_cl` | `kuti_face.f00_e_l_op` |
| 33 | `mouth/33` | Vowel O, small | `kuti_face.f00_n_s_cl` | `kuti_face.f00_o_s_op` |
| 34 | `mouth/34` | Vowel O, large | `kuti_face.f00_n_l_cl` | `kuti_face.f00_o_l_op` |
| 35 | `mouth/35` | Closed consonant mouth, small | `kuti_face.f00_n_s_cl` | `kuti_face.f00_n_s_cl` |
| 36 | `mouth/36` | Closed consonant mouth, large | `kuti_face.f00_n_l_cl` | `kuti_face.f00_n_l_cl` |
| 37 | `mouth/37` | Cat-like mouth | `kuti_face.f00_neko_cl` | `kuti_face.f00_neko_op` |
| 38 | `mouth/38` | Triangular mouth | `kuti_face.f00_san_op` | `kuti_face.f00_san_op` |
| 39 | `mouth/39` | Beaming smile | `kuti_face.f00_niko_cl` | `kuti_face.f00_niko_op` |
| 40 | `mouth/40` | Eating mouth | `kuti_face.f00_mogu_op` | `kuti_face.f00_tabe_op` |
| 41 | `mouth/41` | Chewing mouth | `kuti_face.f00_mogu_cl` | `kuti_face.f00_mogu_op` |
| 42 | `mouth/42` | Dissatisfied closed mouth | `kuti_face.f00_human_cl` | `kuti_face.f00_human_cl` |

### Continuous controls

| Key | Type | Meaning |
| --- | --- | --- |
| eyesOpen | number 0-1 | Eyelid opening limit; 0 closed, 1 fully open |
| mouthOpen | number 0-1 | Mouth opening; 0 closed, 1 fully open |
| blink | boolean | Enable automatic native blinking |


## Animator states

Use `play_animation.state`. The signed ID is `Animator.StringToHash(key)` on layer 0, not a scheduler action ID. Animation playback does not perform gameplay tasks, props or navigation. The runtime confirms each listed state exists; the discovery list is not a complete Studio animation library. These examples use the roaming model. Native talk uses another Animator; refresh `animation_catalog` after entering or leaving a conversation and check each entry's `context`. `animation.state` and hashes report actual playback.

| Signed state hash | State key | English meaning |
| --- | --- | --- |
| -437354176 | `Appearance` | Grooming (Appearance) |
| 1827410204 | `Appearance2` | Grooming (Appearance2) |
| 468394378 | `Appearance3` | Grooming (Appearance3) |
| -225934145 | `Appearance5` | Grooming (Appearance5) |
| 1803670789 | `Appearance6` | Grooming (Appearance6) |
| 1838035980 | `ChangeMind` | Taking a break (ChangeMind) |
| 744289238 | `ChangeMind10` | Taking a break (ChangeMind10) |
| 1532740416 | `ChangeMind11` | Taking a break (ChangeMind11) |
| -1034775814 | `ChangeMind12` | Taking a break (ChangeMind12) |
| 319716370 | `ChangeMind2` | Taking a break (ChangeMind2) |
| 1678331012 | `ChangeMind3` | Taking a break (ChangeMind3) |
| -93464281 | `ChangeMind4` | Taking a break (ChangeMind4) |
| -1922373199 | `ChangeMind5` | Taking a break (ChangeMind5) |
| 342079499 | `ChangeMind6` | Taking a break (ChangeMind6) |
| 1667532957 | `ChangeMind7` | Taking a break (ChangeMind7) |
| -203713268 | `ChangeMind8` | Taking a break (ChangeMind8) |
| -2065914470 | `ChangeMind9` | Taking a break (ChangeMind9) |
| 541959475 | `Club_Cheer` | Cheer club practice (Club_Cheer) |
| 1009566468 | `Club_Cheer1` | Cheer club practice (Club_Cheer1) |
| -1524264258 | `Club_Cheer2` | Cheer club practice (Club_Cheer2) |
| -769482200 | `Club_Cheer3` | Cheer club practice (Club_Cheer3) |
| 1972151244 | `Club_Comic` | Manga club activity (Club_Comic) |
| -2005746635 | `Club_Comic2` | Manga club activity (Club_Comic2) |
| 1838661728 | `Club_Koikatu` | Koikatsu club activity (Club_Koikatu) |
| 1460291733 | `Club_Koikatu2` | Koikatsu club activity (Club_Koikatu2) |
| 537753603 | `Club_Koikatu3` | Koikatsu club activity (Club_Koikatu3) |
| -301256027 | `Club_Land` | Athletics club practice (Club_Land) |
| -934837043 | `Club_Swim` | Swimming club practice (Club_Swim) |
| -3651708 | `Club_Swim2` | Swimming club practice (Club_Swim2) |
| -320875747 | `Club_Tea` | Tea ceremony club activity (Club_Tea) |
| 2034175239 | `Club_Tea2` | Tea ceremony club activity (Club_Tea2) |
| 238566801 | `Club_Tea3` | Tea ceremony club activity (Club_Tea3) |
| -645213589 | `Dance` | Dance practice (Dance) |
| -1066628081 | `Dance2` | Dance practice (Dance2) |
| -1911024371 | `Dinner` | Eating a meal (Dinner) |
| 1693112997 | `Dinner2` | Eating a meal (Dinner2) |
| 334359091 | `Dinner3` | Eating a meal (Dinner3) |
| -864063531 | `Drink` | Drinking (Drink) |
| -1683499089 | `Drink3` | Drinking (Drink3) |
| 96682508 | `Drink4` | Drinking (Drink4) |
| -2095472974 | `Game` | Playing a phone game (Game) |
| 1639850708 | `Houshi2` | Intimate activity (Houshi2) |
| 381227586 | `Houshi3` | Intimate activity (Houshi3) |
| -1998747679 | `Houshi4` | Intimate activity (Houshi4) |
| -2443401 | `Houshi5` | Intimate activity (Houshi5) |
| 2081823275 | `Idle` | Standing idle (Idle) |
| -1304433860 | `kotei_tachi` | Standing idle (kotei_tachi) |
| -129426149 | `Lesbian` | Paired intimate activity (Lesbian) |
| -1874313476 | `Lesbian2` | Paired intimate activity (Lesbian2) |
| -414249366 | `Lesbian3` | Paired intimate activity (Lesbian3) |
| -2089788878 | `Locomotion` | Walking locomotion (Locomotion) |
| -1807062758 | `Locomotion 0` | Walking locomotion (Locomotion 0) |
| -1439380330 | `Locomotion_Anger` | Walking locomotion (Locomotion_Anger) |
| 1634715586 | `Masturbation` | Solo intimate activity (Masturbation) |
| 1876849238 | `Masturbation2` | Solo intimate activity (Masturbation2) |
| 416891584 | `Masturbation3` | Solo intimate activity (Masturbation3) |
| -2034378909 | `Masturbation4` | Solo intimate activity (Masturbation4) |
| 210963790 | `Music` | Listening to music (Music) |
| -1950531177 | `Music2` | Listening to music (Music2) |
| -54890239 | `Music3` | Listening to music (Music3) |
| -2054244135 | `Phone` | Using a phone (Phone) |
| -446735675 | `Phone2` | Using a phone (Phone2) |
| -1839699373 | `Phone3` | Using a phone (Phone3) |
| 205324272 | `Phone4` | Using a phone (Phone4) |
| 245876189 | `Reading` | Reading (Reading) |
| -491145311 | `Reading2` | Reading (Reading2) |
| -1782675657 | `Reading3` | Reading (Reading3) |
| 198840980 | `Reading4` | Reading (Reading4) |
| 1877016939 | `Shooting` | Taking a selfie (Shooting) |
| 115405060 | `Shower` | Showering (Shower) |
| -822940248 | `Sleep` | Sleeping (Sleep) |
| 1528772224 | `Sleep1` | Sleeping (Sleep1) |
| -1038719174 | `Sleep2` | Sleeping (Sleep2) |
| -1257154644 | `Sleep3` | Sleeping (Sleep3) |
| 729137679 | `Sleep4` | Sleeping (Sleep4) |
| -616251178 | `Sport` | Exercising (Sport) |
| 1975422252 | `Sport2` | Exercising (Sport2) |
| 45727162 | `Sport3` | Exercising (Sport3) |
| -1663227879 | `Sport4` | Exercising (Sport4) |
| -338028401 | `Sport5` | Exercising (Sport5) |
| 1926449461 | `Sport6` | Exercising (Sport6) |
| 666810445 | `Study` | Studying (Study) |
| 666486598 | `suwari_Idle` | Seated idle |
| -1624975941 | `talk` | Conversation, speaking |
| -1951916837 | `talk_uke` | Conversation, listening |
| 1975430100 | `Toilet` | Using a toilet (Toilet) |

## Regenerating the reference

Save a fresh `catalog` result after loading the actor, then run:

```powershell
python -X utf8 tools/export_reference.py --catalog catalog.json
```

This command regenerates the English reference. Update the corresponding tables in the [Chinese reference](REFERENCE.zh-CN.md) with the same IDs, keys, hashes and morph targets; translate explanations only.

Runtime request templates contain the current story generation. Obtain fresh templates when calling the API.
