using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using VRC.SDKBase;

namespace OutfitSwitcher
{
    public enum OutfitCategory
    {
        /// <summary>通常メニューに表示する</summary>
        Public,
        /// <summary>制限メニュー（Private）に分けて表示する</summary>
        Restricted,
    }

    public enum OutfitGroupMode
    {
        /// <summary>グループ内の衣装をどれか1つだけ表示する（Int パラメータ）</summary>
        Exclusive,
        /// <summary>グループ内の衣装をそれぞれ個別に ON/OFF する（Bool パラメータ）</summary>
        Toggle,
    }

    public enum OutfitEntryKind
    {
        /// <summary>複数の衣装をまとめたグループ</summary>
        Group,
        /// <summary>単体の ON/OFF ボタン（衣装1つ、サブメニューなし）</summary>
        Button,
    }

    [Serializable]
    public class OutfitItem
    {
        /// <summary>連動オブジェクトの条件から参照するための ID。並べ替えても変わらない（空欄は未参照）</summary>
        public string id = "";

        public string label = "";
        public Texture2D icon;
        public List<GameObject> targets = new List<GameObject>();

        /// <summary>Toggle モードでの初期値</summary>
        public bool defaultOn;

        /// <summary>ボタンだけ制限メニューに置く（パラメータはグループと共有のまま）</summary>
        public bool restricted;

        /// <summary>Quest（Android / iOS）ビルドでは使わない。パラメータの値は残し、選ばれたら fallbackIndex の衣装を表示する</summary>
        public bool pcOnly;

        /// <summary>この衣装が使えない時（Quest で PC のみ、対象オブジェクトなし）に表示する衣装。-1 はグループの初期衣装</summary>
        public int fallbackIndex = -1;

        /// <summary>衣装内で個別に ON/OFF するオブジェクト。1つ以上あると、衣装名のサブメニューにまとめる</summary>
        public List<OutfitPart> parts = new List<OutfitPart>();
    }

    /// <summary>衣装内のパーツ。衣装の対象オブジェクトの子にある前提（衣装を脱ぐと親ごと非表示になる）</summary>
    [Serializable]
    public class OutfitPart : ISerializationCallbackReceiver
    {
        /// <summary>連動オブジェクトの条件から参照するための ID（空欄は未参照）</summary>
        public string id = "";

        public string label = "";
        public Texture2D icon;

        /// <summary>対象オブジェクトと、パーツを ON にした時に表示するか（false なら ON で隠す）</summary>
        public List<OutfitPartObject> objects = new List<OutfitPartObject>();

        public bool defaultOn = true;

        /// <summary>パラメータを保存するか。同期はグループの設定に従う</summary>
        public bool saved = true;

        /// <summary>旧形式（GameObject のリスト）。読み込み時に objects へ移す</summary>
        [SerializeField, FormerlySerializedAs("targets"), HideInInspector]
        private List<GameObject> legacyTargets = new List<GameObject>();

        public void OnBeforeSerialize()
        {
        }

        public void OnAfterDeserialize()
        {
            if (legacyTargets == null || legacyTargets.Count == 0) return;
            if (objects == null) objects = new List<OutfitPartObject>();
            foreach (var target in legacyTargets)
                objects.Add(new OutfitPartObject { target = target, active = true });
            legacyTargets.Clear();
        }
    }

    [Serializable]
    public class OutfitPartObject
    {
        public GameObject target;

        /// <summary>true: パーツ ON で表示 / OFF で非表示。false: パーツ ON で非表示 / OFF で表示</summary>
        public bool active = true;
    }

    public enum LinkConnector
    {
        And,
        Or,
    }

    public enum LinkSourceKind
    {
        /// <summary>衣装・ボタン（排他グループなら「選択中」、それ以外は ON）</summary>
        Item,
        /// <summary>衣装内のパーツが ON</summary>
        Part,
        /// <summary>制限メニューのロック解除が ON</summary>
        Unlock,
    }

    /// <summary>連動オブジェクトの条件の1行。1行目は IF（connector は使わない）、2行目以降は AND / OR でつなぐ</summary>
    [Serializable]
    public class LinkCondition
    {
        public LinkConnector connector = LinkConnector.And;
        public bool not;
        public LinkSourceKind kind = LinkSourceKind.Item;

        /// <summary>OutfitItem.id または OutfitPart.id（Unlock では使わない）</summary>
        public string sourceId = "";
    }

    /// <summary>条件を満たした時に objects を指定の状態にし、満たさない時は逆にする。AND は OR より先に結びつく</summary>
    [Serializable]
    public class LinkRule
    {
        public string name = "";
        public List<LinkCondition> conditions = new List<LinkCondition>();
        public List<OutfitPartObject> objects = new List<OutfitPartObject>();
    }

    /// <summary>メニュー項目。グループ、または単体のボタン（items が1つの Toggle）</summary>
    [Serializable]
    public class OutfitGroup
    {
        public OutfitEntryKind kind = OutfitEntryKind.Group;
        public string name = "";
        public Texture2D icon;
        public OutfitCategory category = OutfitCategory.Public;
        public OutfitGroupMode mode = OutfitGroupMode.Exclusive;

        /// <summary>true ならグループ名のサブメニューを作る。false なら親メニューに直接並べる</summary>
        public bool useSubmenu;

        /// <summary>空欄なら prefix + グループ名 を自動で使う</summary>
        public string parameterName = "";
        public bool saved = true;
        public bool synced = true;

        /// <summary>Exclusive モード: 初期衣装がある時に、全部 OFF にする「なし」ボタンをメニューに追加するか</summary>
        public bool showNoneButton;

        /// <summary>Exclusive モードの初期衣装。-1 は「なし」</summary>
        public int defaultIndex = -1;

        public List<OutfitItem> items = new List<OutfitItem>();
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("Maka's Tools/OutfitSwitcher Settings")]
    public class OutfitSwitcherSettings : MonoBehaviour, IEditorOnly
    {
        // --- 通常メニュー ---
        /// <summary>生成先。VRCExpressionsMenu / MA Menu Item・Menu Group を持つ GameObject / null（ルートに自動生成）</summary>
        public UnityEngine.Object menuTarget;
        public string menuName = "Outfits";
        public Texture2D menuIcon;
        /// <summary>生成先を指定した時、サブメニューを作らず生成先に直接並べる</summary>
        public bool flattenPublicMenu = true;

        // --- 制限メニュー ---
        /// <summary>null なら通常メニューの中に作る</summary>
        public UnityEngine.Object restrictedMenuTarget;
        /// <summary>生成先を指定した時、サブメニューを作らず生成先に直接並べる</summary>
        public bool flattenRestrictedMenu = true;
        public string restrictedMenuName = "Private";
        public Texture2D restrictedMenuIcon;
        public bool useRestrictedLock;
        public string unlockLabel = "ロック解除";
        public Texture2D unlockIcon;

        // --- Quest ---
        /// <summary>Quest ビルドで「PC のみ」衣装の対象オブジェクトを削除する</summary>
        public bool removePcOnlyObjects = true;

        // --- パラメータ ---
        public string parameterPrefix = "OutfitSwitcher/";

        public List<OutfitGroup> groups = new List<OutfitGroup>();

        // --- 高度な設定 ---
        /// <summary>連動オブジェクト。既存のパラメータから判定するので、パラメータは増えない</summary>
        public List<LinkRule> linkRules = new List<LinkRule>();
    }
}
