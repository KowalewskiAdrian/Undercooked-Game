using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Undercooked.UI
{
    public class OrderManager : MonoBehaviour
    {

        [SerializeField] private LevelData currentLevel;
        private readonly List<Order> _orders = new List<Order>();
        private readonly Queue<Order> _poolOrders = new Queue<Order>();
        
        private WaitForSeconds _intervalBetweenDropsWait;
        private bool _isGeneratorActive;
        
        [SerializeField] private Order orderPrefab;
        [SerializeField] private float intervalBetweenDrops = 5f;
        [SerializeField] private int maxConcurrentOrders = 5;
        [SerializeField] private OrdersPanelUI ordersPanelUI;
        
        private Coroutine _generatorCoroutine;
        
        public delegate void OrderSpawned(Order order);
        public static event OrderSpawned OnOrderSpawned;

        public delegate void OrderExpired(Order order);
        public static event OrderExpired OnOrderExpired;
        
        public delegate void OrderDelivered(Order order, int tipCalculated);
        public static event OrderDelivered OnOrderDelivered;

        private Order GetOrderFromPool()
        {
            return _poolOrders.Count > 0 ? _poolOrders.Dequeue() : Instantiate(orderPrefab, transform);
        }
        
        public void Init(LevelData levelData)
        {
            currentLevel = levelData;
            _orders.Clear();
            _intervalBetweenDropsWait = new WaitForSeconds(intervalBetweenDrops);
            _isGeneratorActive = true;
            _generatorCoroutine = StartCoroutine(OrderGeneratorCoroutine());
        }

        private void PauseOrderGenerator()
        {
            _isGeneratorActive = false;
            if (_generatorCoroutine != null)
            {
                StopCoroutine(_generatorCoroutine);
            }
        }

        private void ResumeIfPaused()
        {
            _isGeneratorActive = true;
            _generatorCoroutine ??= StartCoroutine(OrderGeneratorCoroutine());
        }
        
        public void StopAndClear()
        {
            PauseOrderGenerator();
            _generatorCoroutine = null;
            _orders.Clear();
            
            Debug.Log("[OrderManager] StopAndClear");
        }

        private IEnumerator OrderGeneratorCoroutine()
        {
            while (_isGeneratorActive)
            {
                TrySpawnOrder();
                yield return _intervalBetweenDropsWait;    
            }
        }

        private void TrySpawnOrder()
        {
            if (_orders.Count < maxConcurrentOrders)
            {
                var order = GetOrderFromPool();
                if (order == null)
                {
                    Debug.LogWarning("[OrderManager] Couldn't pick an Order from pool", this);
                    return;
                }
                
                order.Setup(GetRandomOrderData());
                _orders.Add(order);
                SubscribeEvents(order);
                OnOrderSpawned?.Invoke(order);
            }
        }

        private void SubscribeEvents(Order order)
        {
            order.OnDelivered += HandleOrderDelivered;
            order.OnExpired += HandleOrderExpired;
        }
        
        private void UnsubscribeEvents(Order order)
        {
            order.OnDelivered -= HandleOrderDelivered;
            order.OnExpired -= HandleOrderExpired;
        }

        private void HandleOrderDelivered(Order order)
        {
            DeactivateSendBackToPool(order);
        }

        private void DeactivateSendBackToPool(Order order)
        {
            UnsubscribeEvents(order);
            order.SetOrderDelivered();
            _orders.RemoveAll(x => x.IsDelivered);
            _poolOrders.Enqueue(order);
        }

        private OrderData GetRandomOrderData()
        {
            var randomIndex = Random.Range(0, currentLevel.orders.Count);
            return Instantiate(currentLevel.orders[randomIndex]);
        }

        private static void HandleOrderExpired(Order order)
        {
            OnOrderExpired?.Invoke(order);
        }

        public void CheckIngredientsMatchOrder(List<Ingredient> ingredients)
        {
            if (ingredients == null) return;
            List<IngredientType> plateIngredients = ingredients.Select(x => x.Type).ToList();

            // orders are checked by arrival order (arrivalTime is reset when order expires)
            List<Order> orderByArrivalNotDelivered = _orders
                .Where(x => x.IsDelivered == false)
                .OrderBy(x => x.ArrivalTime).ToList();
            
            for (int i = 0; i < orderByArrivalNotDelivered.Count; i++)
            {
                var order = orderByArrivalNotDelivered[i];

                List<IngredientType> orderIngredients = order.Ingredients.Select(x => x.type).ToList();

                if (plateIngredients.Count != orderIngredients.Count) continue;
                
                var intersection = plateIngredients.Except(orderIngredients).ToList();
                if (intersection.Count == 0)
                {
                    Debug.Log($"[OrderManager] order{i} MATCH the plate");
                    var tip = CalculateTip(order);
                    DeactivateSendBackToPool(order);
                    OnOrderDelivered?.Invoke(order, tip);
                    ordersPanelUI.RegroupPanelsLeft();
                    return;
                }
                else
                {
                    Debug.Log($"[OrderManager] order#{i} doesn't match plate");
                }
            }
        }

        private static int CalculateTip(Order order)
        {
            var ratio = order.RemainingTime / order.InitialRemainingTime;
            if (ratio > 0.75f) return 6;
            if (ratio > 0.5f) return 4;
            return ratio > 0.25f ? 2 : 0;
        }
    }
    
}
