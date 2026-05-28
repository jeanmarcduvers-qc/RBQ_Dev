declare @CodeQualif varchar(20)
		,@AnConstDeb varchar(10)
		,@AnConstfin varchar(20)
		,@AnNull varchar(20)
		,@Annee_role varchar(20)
		,@CUBF varchar(20)
		,@CodeGeo varchar(20)
		,@cadastre varchar(20)
		,@nom varchar(20)
		,@prenom varchar(20)
		,@etageMin varchar(20)
		,@etageMax varchar(20)
		,@aireMin varchar(20)
		,@aireMax varchar(20)
		,@inclureCh varchar(20)
		,@inclureLogement varchar(20)
		,@chambreMin varchar(20)
		,@chambreMax varchar(20)
		,@nonResMin varchar(20)
		,@nonResMax varchar(20)
		,@REGION varchar(20)
		,@MRC varchar(20)
		,@munic varchar(20)
		,@Matricule varchar(20)
		


select @CodeQualif = 'Tous'
	,@AnConstDeb = '2023'
	,@AnConstfin = '2023'
	,@AnNull = 'Non'
	,@Annee_role = 'Tous'
	,@CUBF = '1'
	,@CodeGeo = '000'
	,@cadastre  = '0'
	,@Matricule  = '0'
	,@nom = 'AucunFiltre'
    ,@prenom = 'AucunFiltre'
	,@etageMin = '0'
	,@etageMax = '1000'
	,@aireMin = '0'
	,@aireMax = '10000'
	,@inclureCh = '1'
	,@inclureLogement = '1'
	,@chambreMin = '0'
	,@chambreMax = '1000'
	,@nonResMin = '0'
	,@nonResMax = '1000'
	,@REGION = 'Lanaudière'
	,@MRC = 'Tous'
	,@munic = '000'

use [SDEXT01]

SELECT Distinct str(ROLE_EVAL_FONCIER.EX_AN_ROLE_EVAL,4,0) as [Année rôle evaluation]
	  ,substring (convert(varchar(10),UNITE_EVALUATION.[EX_DH_CRET_ENRG],120),1,4)  as 'Année de chargement des données'
	,UNITE_EVALUATION.[EX_NO_MATR_UNIT_EVAL] as [Numéro matricule]
  
      ,UNITE_EVALUATION.[EX_NO_DOSS_UNIT_EVAL] as [Numéro dossier]
	  ,CADASTRE.EX_NO_LOT_CADS_RENV [Numéro de lot du cadastre du Québec (rénové)]
	  ,CADASTRE.EX_DE_SUFF_LOT_CADS_RENV [Suffixe du numéro de lot du cadastre du Québec (rénové)]
      ,CADASTRE.EX_NM_CADS_NREN [Nom du cadastre non rénové]
      ,CADASTRE.EX_DE_DESG_SECN_CADS_NREN [Désignation secondaire du cadastre non rénové]
      ,CADASTRE.EX_NO_LOT_NREN [Numéro de lot non rénové]
      ,CADASTRE.EX_IN_PART_NSUB_LOT_NREN [Indicateur de partie non subdivisée du lot non rénové]


--	  ,UNITE_EVALUATION.[GI3_CO_ROWD_SITE] as [Numéro site]
      ,ADRESSE_EVAL.[EX_NO_INFR_ADRS_UNIT_EVAL]as [Numéro inférieur adresse]
      ,ADRESSE_EVAL.[EX_DE_SUFF_NUMR_INFR_UNIT] as [Suffixe numéro inférieur adresse]
      ,ADRESSE_EVAL.[EX_NO_SUPR_ADRS_UNIT_EVAL] as [Numéro supérieur adresse]
      ,ADRESSE_EVAL.[EX_DE_SUFF_NUMR_SUPR_UNIT] as [Suffixe numéro supérieur adresse]
      ,ADRESSE_EVAL.[EX_CO_GENR_ADRS_UNIT_EVAL] as [Code générique adresse]
      ,ADRESSE_EVAL.[EX_CO_LIEN_ADRS_UNIT_EVAL]  as [Code lien adresse]	
      ,ADRESSE_EVAL.[EX_NM_VOIE_PUBL_ADRS_UNITE] as [Nom voie publique adresse]
      ,ADRESSE_EVAL.[EX_CO_CARD_ADRS_UNIT_EVAL] as [Code point cardinal adresse]
      ,ADRESSE_EVAL.[EX_NO_LOCL_ADRS_UNIT_EVAL] as [Numéro local adresse]
      ,ADRESSE_EVAL.[EX_DE_SUFF_LOCL_ADRS_UNIT] as [Suffixe local adresse]
      ,ROLE_EVAL_FONCIER.[EX_NM_MUNC] as [Municipalité]
      ,ROLE_EVAL_FONCIER.[EX_CO_GEOG_MUNC] as [Code géographique Municipalité]
	  ,case when ROLE_EVAL_FONCIER.EX_CO_DESG_MUNC = 'CT'
	        then 'Canton'
			when ROLE_EVAL_FONCIER.EX_CO_DESG_MUNC = 'CU'
			then 'Cantons unis'
			when ROLE_EVAL_FONCIER.EX_CO_DESG_MUNC = 'EI'
			then 'Établissement amérindien'
			when ROLE_EVAL_FONCIER.EX_CO_DESG_MUNC = 'GR'
			then 'Gouvernement régional'
			when ROLE_EVAL_FONCIER.EX_CO_DESG_MUNC = 'M' 
			then 'Municipalité'
			when ROLE_EVAL_FONCIER.EX_CO_DESG_MUNC = 'NO'
			then 'Territoire non organisé'
			when ROLE_EVAL_FONCIER.EX_CO_DESG_MUNC = 'P'
			then 'Paroisse'
			when ROLE_EVAL_FONCIER.EX_CO_DESG_MUNC = 'R'
			then 'Réserve indienne'
			when ROLE_EVAL_FONCIER.EX_CO_DESG_MUNC = 'TC'
			then 'Terre réservées au Cris'
			when ROLE_EVAL_FONCIER.EX_CO_DESG_MUNC = 'TL'
			then 'Terres de la catégorie L pour les Inuits'
			when ROLE_EVAL_FONCIER.EX_CO_DESG_MUNC = 'TK'
			then 'Terres réservées au Naskapis'
			when ROLE_EVAL_FONCIER.EX_CO_DESG_MUNC = 'V'
			then 'Ville'
			when ROLE_EVAL_FONCIER.EX_CO_DESG_MUNC = 'VC'
			then 'Village Cris'
			when ROLE_EVAL_FONCIER.EX_CO_DESG_MUNC = 'VK'
			then 'Village naskapi'
			when ROLE_EVAL_FONCIER.EX_CO_DESG_MUNC = 'VL'
			then 'Village'
			else 'Inconnu' end as [Type de municipalité] 
 
	  ,REGION_ADM.AE_NM_REGN_ADMN as 'Région administrative'
	  ,MRC.AE_NM_MRC as 'Nom MRC'
      ,CARACT.[EX_VA_DIMN_LINR_TERR_UNIT] as [Dimension linéaire du terrain en front sur la voie publique]
      ,CARACT.[EX_VA_SUPR_TERR_UNIT_EVAL] as [Superficie du terrain porté au rôle]
      ,CARACT.[EX_NB_ETAG_UNIT_EVAL] as [Nombre étage]
      ,CARACT.[EX_AN_CONS_UNIT_EVAL] as [Année construction]
      ,CARACT.[EX_CO_SOUR_ANN_CONS] as [Code source année construction]
      ,CARACT.[EX_VA_AIRE_BATM_UNIT_EVAL]as [Aire bâtiment]
      ,CARACT.[EX_CO_LIEN_PHYS_UNIT_EVAL]as [Code lien physique]
      ,CARACT.[EX_CO_GENR_CONS_UNIT_EVAL]as [Code genre construction]
      ,CARACT.[EX_NB_LOGM_UNIT_EVAL] as [Nombre logement]
      ,CARACT.[EX_NB_CHAM_LOCT_UNIT_EVAL]as [Nombre chambre locative]
      ,CARACT.[EX_NB_LOCL_NRES_UNIT_EVAL]as [Nombre local non-résidentiel]
--,'à ajouter' [Code de la sous-catégorie des immeubles non résidentiels]
--,'à ajouter' [Proportion, exprimée en pourcentage, de la valeur de la partie non résidentielle associée à la sous-catégorie]
      ,CODE_QUALIF.EX_CO_USAG_BIEN_FONC as [CUBF]	  

	  ,case when @CodeQualif <> '000'
	        then @CodeQualif
			else CODE_QUALIF.EX_CO_QUAL_RBQ end  as [Ensemble des codes qualification]


      ,PROPRIETAIRE.EX_NM_PROP_UNIT_EVAL	[Nom légal du propriétaire]
      ,PROPRIETAIRE.EX_PR_PROP_UNIT_EVAL [Prénom du propriétaire]
      ,PROPRIETAIRE.EX_AD_PROP_UNIT_EVAL [Adresse postale non structurée du propriétaire]
      ,PROPRIETAIRE.EX_NM_MUNC_PROP_UNIT_EVAL [Nom de la municipalité de l’adresse postale du propriétaire]
	  ,PROPRIETAIRE.EX_CO_POST_PROP_UNIT_EVAL [Code postal de l’adresse postale du propriétaire]
      ,convert(varchar(10),PROPRIETAIRE.EX_DD_INSC_PROP_UNIT,120) [Date initiale d’inscription au rôle du propriétaire concerné]

      ,PROPRIETAIRE.EX_CO_STAT_IMPS_SCOL_PROP  [Statut du propriétaire aux fins d’imposition scolaire]
      ,PROPRIETAIRE.EX_NO_CIVQ_ADRS_PROP_UNIT  [Numéro civique de l’adresse postale du propriétaire]
      ,PROPRIETAIRE.EX_DE_SUFF_NUMR_ADRS_PROP [Fraction ou partie de l’adresse postale du propriétaire]
      ,PROPRIETAIRE.EX_CO_GENR_ADRS_PROP_UNIT [Code de générique de l’adresse postale du propriétaire]
      ,PROPRIETAIRE.EX_NM_VOIE_PUBL_PROP_UNIT [Nom de la voie publique de l’adresse postale du propriétaire]
      ,PROPRIETAIRE.EX_CO_POIN_CARD_PROP_UNIT [Code du point cardinal de l’adresse postale du propriétaire]
      ,PROPRIETAIRE.EX_NO_LOCL_PROP_UNIT_EVAL [Numéro d’appartement ou de local de l’adresse postale du propriétaire]
      ,PROPRIETAIRE.EX_DE_SUFF_LOCL_PROP_UNIT [Fraction ou partie d’adresse du numéro d’appartement ou de local de l’adresse postale du propriétaire]
      ,PROPRIETAIRE.EX_NM_PROV_PROP_UNIT_EVAL [Province ou état de l’adresse postale du propriétaire]
      ,PROPRIETAIRE.EX_NM_PAYS_PROP_UNIT_EVAL [Pays de l’adresse postale du propriétaire]
      ,PROPRIETAIRE.EX_NO_CASE_POST_PROP_UNIT [Case postale de la succursale postale]
      ,PROPRIETAIRE.EX_NM_SUCC_POST_PROP_UNIT [Succursale postale]
      ,PROPRIETAIRE.EX_CO_INSC_PROP_UNIT_EVAL [Code des conditions d’inscription]

	  
 
 
 
  FROM [SDEXT01].[EXT2].[EX2_UNITE_EVALUATION] UNITE_EVALUATION


  inner join [SDEXT01].[EXT2].[EX2_CARC_UNITE_EVAL] CARACT
    on CARACT.EX_NS_UNIT_EVAL = UNITE_EVALUATION.EX_NS_UNIT_EVAL


inner join  [SDEXT01].[EXT2].[EX2_QUALIF_USAGE] CODE_QUALIF 
	 on CODE_QUALIF.EX_CO_USAG_BIEN_FONC=UNITE_EVALUATION.EX_CO_USAG_BIEN_FONC


inner join [SDEXT01].[EXT2].[EX2_ADRESSE_EVAL] ADRESSE_EVAL
  on ADRESSE_EVAL.EX_NS_UNIT_EVAL = UNITE_EVALUATION.EX_NS_UNIT_EVAL


inner join [SDEXT01].[EXT2].[EX2_ROLE_EVAL_FONCIER] ROLE_EVAL_FONCIER
     on  ROLE_EVAL_FONCIER.[EX_NS_ROLE_EVAL]=UNITE_EVALUATION.EX_NS_ROLE_EVAL



inner join [SDEXT01].[EXT2].[EX2_CADASTRE_UNIT_EVAL] CADASTRE
     on CADASTRE.EX_NS_UNIT_EVAL = UNITE_EVALUATION.EX_NS_UNIT_EVAL


 

inner join [SDEXT01].[EXT2].[EX2_PROP_UNITE_EVAL] PROPRIETAIRE
     on PROPRIETAIRE.EX_NS_UNIT_EVAL = UNITE_EVALUATION.EX_NS_UNIT_EVAL
    and PROPRIETAIRE.EX_DD_INSC_PROP_UNIT = (select max(p2.EX_DD_INSC_PROP_UNIT)
                                    from   [SDEXT01].[EXT2].[EX2_PROP_UNITE_EVAL]   p2
                                   where  p2.[EX_NS_UNIT_EVAL] = PROPRIETAIRE.[EX_NS_UNIT_EVAL]
                                     and    p2.EX_NM_PROP_UNIT_EVAL = PROPRIETAIRE.EX_NM_PROP_UNIT_EVAL)





/*   Code region administrative et MRC */

 
inner join [SDGIC01].[AEB].[AE1_MUNP] MUNIC
  on MUNIC.[AE_C_MUNC] = cast(ROLE_EVAL_FONCIER.EX_CO_GEOG_MUNC as int)
 and substring(ROLE_EVAL_FONCIER.EX_CO_GEOG_MUNC,1,2) <> 'NR'
 

inner join [SDGIC01].[AEB].[AE1_REGN_ADMN] REGION_ADM
 on REGION_ADM.AE_C_REGN_ADMN = MUNIC.AE_C_REGN_ADMN
 and substring(ROLE_EVAL_FONCIER.EX_CO_GEOG_MUNC,1,2) <> 'NR'
 


  inner join [SDGIC01].[AEB].[AE1_MRC] MRC
   on MRC.AE_C_MRC = MUNIC.AE_C_MRC
  and substring(ROLE_EVAL_FONCIER.EX_CO_GEOG_MUNC,1,2) <> 'NR'
 
   
 
where ((CARACT.EX_AN_CONS_UNIT_EVAL >= @AnConstDeb and  CARACT.EX_AN_CONS_UNIT_EVAL <= @AnConstFin) 
                    or 
	   (@AnNull='Oui' and CARACT.EX_AN_CONS_UNIT_EVAL is null))
 
   and ( 'Tous' in (@Annee_role)  OR  substring(convert(varchar(10),UNITE_EVALUATION.[EX_DH_CRET_ENRG],120),1,4) = (@Annee_role))
   and (str(ROLE_EVAL_FONCIER.[EX_CO_GEOG_MUNC], 5,0) = @CodeGeo  or @CodeGeo='000')

     AND ( 'Tous' in (@CodeQualif)  OR  CODE_QUALIF.EX_CO_QUAL_RBQ = (@CodeQualif))
  
 --  and ('000' in (@munic) OR ROLE_EVAL_FONCIER.[EX_CO_GEOG_MUNC] in (@munic) )

  
 -- and ('000' in (@CUBF) OR str(CODE_QUALIF.EX_CO_USAG_BIEN_FONC,4,0) in (@CUBF) )
 
    and (@CUBF is null or @CUBF = ' ' or CODE_QUALIF.EX_CO_USAG_BIEN_FONC like @CUBF+'%')
 

 
   and (CADASTRE.EX_NO_LOT_CADS_RENV = @cadastre or @cadastre = 0)
   and (UNITE_EVALUATION.EX_NO_FUS_MATR_UNIT_EVAL = @Matricule or @Matricule = 0)
 
   and (upper(PROPRIETAIRE.EX_NM_PROP_UNIT_EVAL)=upper(@nom) or @nom='AucunFiltre')
   and (upper(PROPRIETAIRE.EX_PR_PROP_UNIT_EVAL)=upper(@prenom) or @prenom='AucunFiltre')
   and ((isnull(CARACT.EX_NB_ETAG_UNIT_EVAL,0)>=@etageMin and isnull(CARACT.EX_NB_ETAG_UNIT_EVAL,0)<=@etageMax))
  and ((isnull(CARACT.EX_VA_AIRE_BATM_UNIT_EVAL,0)>=@aireMin and isnull(CARACT.EX_VA_AIRE_BATM_UNIT_EVAL,0)<=@aireMax))
  and ((isnull(CARACT.EX_NB_CHAM_LOCT_UNIT_EVAL,0)*@inclureCh+(isnull(CARACT.EX_NB_LOGM_UNIT_EVAL,0)*@inclureLogement)))>=@chambreMin 
  and (isnull(CARACT.EX_NB_CHAM_LOCT_UNIT_EVAL,0)*@inclureCh+(isnull(CARACT.EX_NB_LOGM_UNIT_EVAL,0)*@inclureLogement)<=@ChambreMax)
  and ((isnull(CARACT.EX_NB_LOCL_NRES_UNIT_EVAL,0)>=@nonResMin and isnull(CARACT.EX_NB_LOCL_NRES_UNIT_EVAL,0)<=@nonResMax))
 
  and   (@REGION = 'Tous'  or @REGION = REGION_ADM.AE_NM_REGN_ADMN)
     AND ( 'Tous' in (@MRC)  OR  MRC.AE_NM_MRC in  (@MRC))
 

 
 
  
  order by 1